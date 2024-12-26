namespace RafRaft.Domain
{
   using RafRaft.Domain.Messages;
   using Microsoft.Extensions.Logging;
   using System.Runtime.InteropServices;

   // TODO implement stop & relaunch with state saved. POssibly use cancellation token to WebApp

   public class RaftNode<TState, TDataIn, TDataOut>
     where TState : IRaftStateMachine<TDataIn, TDataOut>, new()
     where TDataIn : notnull
     where TDataOut : notnull
   {
      public enum State
      {
         Follower,
         Candidate,
         Leader
      }

      public readonly int id;
      protected readonly IRaftMediator<TDataIn> _mediator;
      protected readonly TState internalState;
      protected readonly Dictionary<int, bool> peersStatus;
      protected readonly ILogger _logger;

      protected State nodeState = State.Follower;
      protected int currentTerm = 0;
      protected int? leaderId = null;

      protected int? _votedFor = null;
      protected int? VotedFor
      {
         get => _votedFor;
         set
         {
            _votedFor = value;
         }
      }
      protected int QuorumNum => (peersStatus.Count + 1) / 2 + 1;
      protected int votesGot = 0;
      protected int ActiveNodesNumber => peersStatus.Where(kvPair => kvPair.Value).Count() + 1; // where peerStatus is true (peer is active)

      protected readonly List<RaftLogEntry<TDataIn>> log = [new RaftLogEntry<TDataIn>(0, 0, default)]; // sentinel entry to retreive info for regular append entries
      protected int commitIndex = 0;
      protected int LastLogIndex => log.Count - 1;
      protected int LastLogTerm => log[^1].Term;
      protected int lastApplied = 0;
      protected Dictionary<int, int> nextIndex = [];
      protected Dictionary<int, int> matchIndex = [];

      protected readonly long broadcastTimeout;
      protected readonly Timer broadcastTimer;
      protected readonly int minElectionTimeout;
      protected readonly int maxElectionTimeout;
      protected readonly Timer electionTimer;
      protected readonly Random _random = new Random();

      protected bool canReceiveRpcs = false; // crutch because server just won't start in time

      protected Lock applyLock = new Lock();
      protected CancellationTokenSource broadcastCancel = new CancellationTokenSource();

      public RaftNode(RaftNodeConfig config, IRaftMediator<TDataIn> mediator, ILogger logger, bool isInitNode)
      {
         broadcastTimeout = config.BroadcastTime;
         minElectionTimeout = config.MinElectionMillis;
         maxElectionTimeout = config.MaxElectionMillis;

         broadcastTimer = new Timer(OnBroadcastElapsed, null, Timeout.Infinite, broadcastTimeout);
         electionTimer = new Timer(OnElectionElapsed, null, Timeout.Infinite, Timeout.Infinite);

         internalState = new TState();

         id = config.Id;

         peersStatus = [];
         foreach (int id in config.PeersIds)
         {
            peersStatus[id] = true;
         }

         _mediator = mediator;
         _logger = logger;

         // crutch because server just won't start in time, but at least one node must
         if (isInitNode)
         {
            canReceiveRpcs = true;
         }
      }

      public void StartUp()
      {
         StartUp(new Dictionary<int, bool>());
      }

      public void StartUp(IDictionary<int, bool> actualPeersStatus)
      {
         foreach (int id in actualPeersStatus.Keys)
         {
            peersStatus[id] = actualPeersStatus[id];
         }
         RestartElectionTimer();
      }

      #region AppendEntries
      public AppendEntriesReply HandleAppendEntriesRequest(AppendEntriesRequest<TDataIn> request)
      {
         _logger.LogInformation("Received AppendEntries request from {id} (Term {term})", request.LeaderId, request.Term);
         canReceiveRpcs = true;

         if (request.Term < currentTerm)
         {
            _logger.LogInformation("AppendEntries rejected, because sender's Term is lower than node's");
            return new AppendEntriesReply(currentTerm, false);
         }

         TryBecomingFollower(request.Term, request.LeaderId);

         RestartElectionTimer();

         if (LastLogIndex < request.PrevLogId || LastLogTerm != request.PrevLogTerm)
         {
            _logger.LogInformation("AppendEntries rejected, because follower doesn't have previous entries");
            return new AppendEntriesReply(currentTerm, false);
         }

         // _logger.LogTrace("Log before: {log}", log);
         log.AddRange(request.Entries);
         if (request.Entries.Count != 0)
         {
            _logger.LogInformation("Logged: {logs}", request.Entries);
         }
         // _logger.LogTrace("Log after: {log}", log);

         if (commitIndex < request.LeaderCommitId)
         {
            commitIndex = Math.Min(request.LeaderCommitId, LastLogIndex); // update commitIndex preventing out of bounds for log
         }
         ApplyCommited();

         return new AppendEntriesReply(currentTerm, true);
      }

      protected Task<AppendEntriesReply> SendAppendEntries(
         int receiverId,
         AppendEntriesRequest<TDataIn> request)
      {
         _logger.LogInformation("Send AppendEntries request to {id} (Term {term})", receiverId, currentTerm);
         if (request.Entries.Count != 0)
         {
            _logger.LogTrace("With entries: {entries}", request.Entries);
         }
         return _mediator.SendAppendEntries(receiverId, request);
      }

      protected async Task HandleAppendEntriesReply(int replierID, AppendEntriesReply reply)
      {
         TryBecomingFollower(reply.Term, replierID);

         if (nodeState != State.Leader)
         {
            return;
         }

         // _logger.LogTrace("Before: nextIndex[{id}]={n}; matchIndex[{id}]={m}", replierID, nextIndex[replierID], replierID, matchIndex[replierID]);
         if (reply.Success)
         {
            _logger.LogTrace("Old nextIndex[{node}]: {nextId}", replierID, nextIndex[replierID]);
            nextIndex[replierID] = Math.Min(nextIndex[replierID] + 1, LastLogIndex + 1);
            _logger.LogTrace("New nextIndex[{node}]: {nextId}", replierID, nextIndex[replierID]);
            matchIndex[replierID] = Math.Min(nextIndex[replierID], LastLogIndex);
            if (commitIndex != LastLogIndex)
            {
               await LeaderCommitAndApply();
            }
         }
         else
         {
            nextIndex[replierID] -= 1;
            _logger.LogTrace("Fail replictation. New nextIndex[{node}]: {nextId}", replierID, nextIndex[replierID]);
            matchIndex[replierID] = 0;
         }
         // _logger.LogTrace("After: nextIndex[{id}]={n}; matchIndex[{id}]={m}", replierID, nextIndex[replierID], replierID, matchIndex[replierID]);
      }
      #endregion

      #region RequestVote
      public VoteReply HandleRequestVoteRequest(VoteRequest request)
      {
         _logger.LogInformation("Received RequestVote request from {id} for Term {term}", request.CandidateId, request.Term);
         canReceiveRpcs = true;

         TryBecomingFollower(request.Term, request.CandidateId);

         if (request.Term < currentTerm)
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because Term is lower than node's", request.CandidateId);
            return new VoteReply(currentTerm, false);
         }

         if (VotedFor != null)
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because Node alreay voted for #{chosenId}",
               request.CandidateId,
               VotedFor);
            return new VoteReply(currentTerm, false);
         }

         if (IsLogWorse(request.LastLogId, request.LastLogTerm))
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because its log is worse", request.CandidateId);
            return new VoteReply(currentTerm, false);
         }

         _logger.LogInformation("Vote granted to {candidateId}", request.CandidateId);
         VotedFor = request.CandidateId;
         RestartElectionTimer();
         return new VoteReply(currentTerm, true);
      }

      protected Task<VoteReply> SendRequestVote(int receiverId, VoteRequest request)
      {
         _logger.LogInformation("Send RequestVote request to {id}", receiverId);

         return _mediator.SendRequestVote(receiverId, request);
      }

      protected void HandleRequestVoteReply(int replierId, VoteReply reply)
      {
         TryBecomingFollower(reply.Term, replierId);
         if (nodeState == State.Candidate && reply.VoteGranted)
         {
            votesGot++;
            _logger.LogInformation("Receive vote {num}", votesGot);
         };
      }
      #endregion
      protected async Task LeaderCommitAndApply()
      {
         // _logger.LogTrace("commitIndex: {p}; log.Count: {l}", commitIndex, log.Count);
         for (int possibleCommitId = commitIndex + 1; possibleCommitId < log.Count; possibleCommitId++)
         {
            int numReplicated = matchIndex.Values.Where(replicatedId => replicatedId >= possibleCommitId).Count();
            // _logger.LogTrace("possibleCommitId: {p}; log.Count: {l}; numReplicated: {n}; Quorum: {q}", possibleCommitId, LastLolog.CountgIndex, numReplicated, QuorumNum);
            if (numReplicated + 1 >= QuorumNum)
            {
               if (log[possibleCommitId].Term == currentTerm)
               {
                  commitIndex = possibleCommitId;
               }
            }
            else
            {
               break;
            }
         }

         ApplyCommited();
      }
      // Please update commitIndex before calling
      protected void ApplyCommited()
      {
         // _logger.LogInformation("Try applying: lastApplied: {lastApplied}, commitIndex: {commitIndex}", lastApplied, commitIndex);
         lock (applyLock)
         {
            for (; lastApplied < commitIndex; lastApplied++)
            {
               internalState.Apply(log[lastApplied + 1]);
               _logger.LogInformation("Applied entry {entry}", log[lastApplied + 1]);
            }
         }
      }

      /// <summary>
      /// Change term if new term is larger.
      /// </summary>
      /// <param name="term">New term</param>
      /// <param name="possibleLeaderId">Node's id with new term</param>
      /// <returns>
      /// False if new term isn't larger, didn't convert to follower. 
      /// True if new term is larger, so node was coverted to follower. 
      /// </returns>
      protected bool TryBecomingFollower(int term, int possibleLeaderId)
      {
         if (term <= currentTerm)
         {
            return false;
         }

         currentTerm = term;
         switch (nodeState)
         {
            case State.Follower:
               _logger.LogInformation("Received RPC request or response from node with bigger Term as Follower's.");
               break;
            case State.Candidate:
               _logger.LogInformation("Received RPC request or response from node with at least as large Term as Candidate's. Becoming follower");
               break;
            case State.Leader:
               _logger.LogInformation("Received RPC request or response from node with bigger Term as Leader's. Becoming follower");
               break;
         }
         BecomeFollower(possibleLeaderId);

         return true;
      }

      protected bool IsLogWorse(int otherLogIndex, int otherLogTerm)
      {
         return otherLogTerm < LastLogTerm && otherLogIndex < LastLogIndex;
      }

      protected async void OnBroadcastElapsed(object? Ignored)
      {
         _logger.LogTrace("Broadcast timer elapsed");

         List<Task> taskList = [];
         AppendEntriesRequest<TDataIn> request;
         foreach (int nodeId in peersStatus.Keys)
         {
            _logger.LogTrace("nextIndex[{nodeId}]: {nextIndex}. Total: {couny}", nodeId, nextIndex[nodeId], nextIndex.Keys.Count);
            int prevLogId = nextIndex[nodeId] - 1;
            int prevLogTerm = log[prevLogId].Term;
            List<RaftLogEntry<TDataIn>> entries = [];
            // _logger.LogTrace("Try applying more entries: leader_count - {count}, nextIndex[{node}] - {next}", log.Count, nodeId, nextIndex[nodeId]);
            if (log.Count > nextIndex[nodeId])
            {
               entries.Add(log[nextIndex[nodeId]]);
            }
            request = new AppendEntriesRequest<TDataIn>(currentTerm, id, prevLogId, prevLogTerm, entries, commitIndex);

            taskList.Add(AppendEntries(nodeId, request));
         }

         await Task.WhenAll(taskList);
         _logger.LogTrace("Broadcast finishes");
         if (broadcastCancel.IsCancellationRequested)
         {
            return;
         }
         RestartBroadcastTimer();
      }

      protected async Task AppendEntries(int receiverId, AppendEntriesRequest<TDataIn> request)
      {
         try
         {
            var requestTask = SendAppendEntries(receiverId, request);
            await requestTask.ContinueWith(
               async (task) => await HandleAppendEntriesReply(receiverId, task.Result)
            );

            if (peersStatus[receiverId] == false)
            {
               _logger.LogWarning("Previously inactive {id} received AppendEntries. Marking it as active", receiverId);
            }
            peersStatus[receiverId] = true; // mark receiver as active if everything completes
         }
         catch (Exception e)
         {
            if (peersStatus[receiverId]) // if exception persists -> don't do repeated steps
            {
               _logger.LogWarning("Couldn't send AppendEntries to {id}. Marking it as inactive", receiverId);
               _logger.LogTrace("Connection error message: {message}", e.Message);
               peersStatus[receiverId] = false; // mark receiver as inactive
            }
         }
      }

      protected void RestartElectionTimer()
      {
         electionTimer.Change(GetRandomElectionTime(), Timeout.Infinite);
      }

      protected void RestartBroadcastTimer()
      {
         broadcastTimer.Change(broadcastTimeout, Timeout.Infinite);
      }

      protected async Task BeginElection()
      {
         currentTerm++;
         _logger.LogInformation("Begin election for Term {Term}", currentTerm);

         broadcastTimer.Change(Timeout.Infinite, Timeout.Infinite);
         nodeState = State.Candidate;

         VotedFor = id;
         votesGot = 1;
         leaderId = null;

         List<Task> taskList = [];
         VoteRequest request;
         foreach (int peerId in peersStatus.Keys)
         {
            request = new VoteRequest(currentTerm, id, LastLogIndex, LastLogTerm);

            taskList.Add(TryVoteRequest(peerId, request));
         }
         RestartElectionTimer();
         await Task.WhenAll(taskList);

         // leader was found during election
         if (nodeState != State.Candidate)
         {
            _logger.LogInformation("Premature election stop");
            RestartElectionTimer();
            return;
         }
         _logger.LogInformation("Received all VoteRequest replies from {num} nodes", ActiveNodesNumber);

         if (votesGot >= QuorumNum)
         {
            BecomeLeader();
         }
         else
         {
            _logger.LogInformation("Not enough votes received ({votesGot} / {votesNeeded})", votesGot, QuorumNum);
         }
      }

      protected async Task TryVoteRequest(int receiverId, VoteRequest request)
      {
         try
         {
            var requestTask = SendRequestVote(receiverId, request);
            await requestTask.ContinueWith((task) =>
            {
               HandleRequestVoteReply(receiverId, task.Result);
            });

            if (peersStatus[receiverId] == false)
            {
               _logger.LogWarning("Previously inactive {id} received VoteRequest. Marking it as active", receiverId);
            }
            peersStatus[receiverId] = true; // mark receiver as active if everything completes
         }
         catch (Exception)
         {
            if (peersStatus[receiverId]) // if exception persists -> don't do repeated steps
            {
               _logger.LogWarning("Couldn't send VoteRequest to {id}. Marking it as inactive", receiverId);
               peersStatus[receiverId] = false; // mark receiver as inactive
            }
         }
      }

      protected async void OnElectionElapsed(object? Ignored)
      {
         _logger.LogTrace("Election timer elapsed");

         if (canReceiveRpcs)
         {
            await BeginElection();
         }
         else
         {
            RestartElectionTimer();
         }
         return;
      }

      protected void BecomeLeader()
      {
         _logger.LogInformation("Become leader for Term {term} with {got} / {required} votes", currentTerm, votesGot, ActiveNodesNumber);
         VotedFor = null;
         leaderId = id;
         nodeState = State.Leader;
         electionTimer.Change(Timeout.Infinite, Timeout.Infinite); // stop election timer
         foreach (int nodeId in peersStatus.Keys)
         {
            nextIndex[nodeId] = log.Count;
            matchIndex[nodeId] = 0;
         }
         broadcastTimer.Change(broadcastTimeout, Timeout.Infinite); // start broadcast
      }

      protected void BecomeFollower(int newLeaderId)
      {
         _logger.LogInformation("New Leader is {id}", newLeaderId);
         leaderId = newLeaderId;
         VotedFor = null;
         votesGot = 0;
         broadcastTimer.Change(Timeout.Infinite, Timeout.Infinite);
         RestartElectionTimer();
         nodeState = State.Follower;
      }

      protected int GetRandomElectionTime()
      {
         return _random.Next(minElectionTimeout, maxElectionTimeout);
      }
   }
}