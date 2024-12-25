namespace RafRaft.Domain
{
   using RafRaft.Domain.Messages;
   using Microsoft.Extensions.Logging;

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
      private readonly IRaftMediator<TDataIn> mediator;
      private State nodeState = State.Follower;
      private int _currentTerm = 0;
      private int CurrentTerm
      {
         get => _currentTerm;
         set
         {
            _currentTerm = value;
         }
      }
      private int? _votedFor;
      private int? VotedFor
      {
         get => _votedFor;
         set
         {
            _votedFor = value;
            Voted = true;
         }
      }
      private List<RaftLogEntry<TDataIn>> entriesLog;
      private int commitIndex = 0;
      private int commitTerm = 0;
      private int lastApplied = 0;
      //TODO Implement NextIndex evaluation
      private List<int> nextIndex;
      //TODO Implement MatchIndex evaluation
      private List<int> matchIndex;
      private readonly TState internalState;
      private readonly long broadcastTimeout;
      private readonly Timer broadcastTimer;
      private readonly int minElectionTimeout;
      private readonly int maxElectionTimeout;
      private readonly Timer electionTimer;
      private readonly Dictionary<int, bool> peersStatus;
      private int _votesRequired;
      private int ActiveNodesNumber => peersStatus.Where(kvPair => kvPair.Value).Count() + 1; // where peerStatus is true (peer is active)
      private int leaderId = -1;
      private int votesGot = 0;

      private int _votedBackValue = 0;
      private bool Voted
      {
         get { return Interlocked.CompareExchange(ref _votedBackValue, 1, 1) == 1; }
         set
         {
            if (value) Interlocked.CompareExchange(ref _votedBackValue, 1, 0);
            else Interlocked.CompareExchange(ref _votedBackValue, 0, 1);
         }
      }

      private CancellationTokenSource _currentElectionCancellation = new CancellationTokenSource();
      private CancellationTokenSource _globalCancel = new CancellationTokenSource();
      private readonly ILogger _logger;
      private readonly Lock _electionLock = new Lock();
      private readonly Random _random = new Random();

      public RaftNode(RaftNodeConfig config, IRaftMediator<TDataIn> mediator, ILogger logger)
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
         _votesRequired = (config.PeersIds.Count + 1) / 2 + 1;

         this.mediator = mediator;

         nextIndex = new List<int>(peersStatus.Count);
         matchIndex = new List<int>(peersStatus.Count);

         entriesLog = new List<RaftLogEntry<TDataIn>>();

         _logger = logger;
      }

      #region Heartbeat
      public AppendEntriesReply HandleHeartbeatRequest(AppendEntriesRequest<TDataIn> request)
      {
         _logger.LogTrace("Received Heartbeat request from {id} with Term: {Term}", request.LeaderId, CurrentTerm);
         TryBecomingFollower(request.Term, request.LeaderId);
         if (request.LeaderId == leaderId)
         {
            ResetElectionTimer();
         }
         return new AppendEntriesReply(CurrentTerm, true);
      }

      private Task<AppendEntriesReply> SendHeartbeat(
         int receiverId,
         AppendEntriesRequest<TDataIn> request,
         CancellationToken token)
      {
         _logger.LogTrace("Send Heartbeat request to {id}", receiverId);
         return mediator.SendHeartbeat(receiverId, request, token);
      }

      private void HandleHeartbeatReply(AppendEntriesReply reply)
      {
         // do absolutely nothing
      }
      #endregion

      #region AppendEntries
      public AppendEntriesReply HandleAppendEntriesRequest(AppendEntriesRequest<TDataIn> request)
      {
         _logger.LogInformation("Received AppendEntries request from {id}", request.LeaderId);

         if (request.LeaderId == leaderId)
         {
            ResetElectionTimer();
         }

         #region Term correction
         bool isNewTermLarger = TryBecomingFollower(request.Term, request.LeaderId);
         if (!isNewTermLarger)
         {
            return new AppendEntriesReply(CurrentTerm, false);
         }
         #endregion

         #region Previous log entry discovery
         RaftLogEntry<TDataIn>? prevEntry = entriesLog[request.PrevLogId];
         if (prevEntry is null || prevEntry.Term != request.PrevLogTerm)
         {
            return new AppendEntriesReply(CurrentTerm, false);
         }
         #endregion

         int lastNewEntryId = AppendEntries(request.Entries);

         if (request.LeaderId > commitIndex)
         {
            commitIndex = Math.Min(request.LeaderCommitId, lastNewEntryId);
         }

         ApplyCommited();

         return new AppendEntriesReply(CurrentTerm, true);
      }

      private Task<AppendEntriesReply> SendAppendEntries(
         int receiverId,
         AppendEntriesRequest<TDataIn> request,
         CancellationToken token)
      {
         _logger.LogInformation("Send AppendEntries request to {id}", receiverId);
         return mediator.SendAppendEntries(receiverId, request, token);
      }

      private void HandleAppendEntriesReply(AppendEntriesReply reply)
      {
         _logger.LogTrace("Received AppendEntries reply");
         // TODO logic
      }

      private int AppendEntries(IList<RaftLogEntry<TDataIn>> Entries)
      {
         if (!Entries.Any()) return commitIndex; // or appened idk

         foreach (RaftLogEntry<TDataIn> entry in Entries)
         {
            entriesLog.Add(entry);
            commitIndex = entry.Index;
            commitTerm = entry.Term;
         }

         return commitIndex;
      }
      #endregion

      #region RequestVote
      public VoteReply HandleRequestVoteRequest(VoteRequest request)
      {
         _logger.LogInformation("Received RequestVote request from {id}", request.CandidateId);

         bool isNewTermLarger = TryBecomingFollower(request.Term, request.CandidateId);
         if (isNewTermLarger)
         {
            _logger.LogInformation("Vote granted to {candidateId}, because its Term is higher", request.CandidateId);
            return new VoteReply(CurrentTerm, true);
         }

         if (request.Term < CurrentTerm)
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because Term is too low", request.CandidateId);
            return new VoteReply(CurrentTerm, false);
         }

         if (nodeState == State.Candidate)
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because Node is candidate itself", request.CandidateId);
            return new VoteReply(CurrentTerm, false);
         }

         if (nodeState == State.Leader)
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because Node is Leader", request.CandidateId);
            return new VoteReply(CurrentTerm, false);
         }

         if (Voted)
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because Node alreay voted for #{chosenId}",
               request.CandidateId,
               VotedFor);
            return new VoteReply(CurrentTerm, false);
         }

         if (!IsNewLogBetter(request.LastLogId, request.LastLogTerm))
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because its log is worse", request.CandidateId);
            return new VoteReply(CurrentTerm, false);
         }

         lock (_electionLock)
         {
            _logger.LogInformation("Vote granted to {candidateId}", request.CandidateId);
            VotedFor = request.CandidateId;
            return new VoteReply(CurrentTerm, true);
         }
      }

      private Task<VoteReply> SendRequestVote(int receiverId, VoteRequest request, CancellationToken token)
      {
         _logger.LogInformation("Send RequestVote request to {id}", receiverId);

         return mediator.SendRequestVote(receiverId, request, token);
      }

      private void HandleRequestVoteReply(VoteReply reply)
      {
         if (nodeState == State.Candidate && reply.VoteGranted)
         {
            votesGot++;
            _logger.LogInformation("Receive vote {num}", votesGot);
         };
      }
      #endregion

      #region UserRequest
      /* private async Task HandleUserSetRequest(TDataIn userRequest)
      {
         // TODO: batch SendAppendEntries call
         if (nodeState == State.Follower)
         {
            // TODO redirect
         }
         if (nodeState == State.Candidate)
         {
            // TODO
            await Task.Task;
         }

         commitIndex++;
         RaftLogEntry<TDataIn> entry = CreateLogEntry(userRequest);
         log.Add(entry);

         RaftLogEntry<TDataIn> prevLog = log[^1];
         AppendEntriesRequest<TDataIn> request = new AppendEntriesRequest<TDataIn>(CurrentTerm, id, prevLog.Index, prevLog.Term, [entry], commitIndex);
         List<Task> taskList = [];
         foreach (int followerId in nodeIds)
         {
            var sendTask = SendAppendEntries(followerId, request);
            var handleTask = sendTask.ContinueWith((reply) =>
               HandleAppendEntriesReply(reply.Result));

            taskList.Add(sendTask);
            taskList.Add(handleTask);
         }

         await Task.WhenAll(taskList);
      } */

      private async Task HandleUserGetRequest(string userRequest)
      {
         // TODO implement. Probably need an object to communicate with user
      }
      #endregion

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
         ResetElectionTimer();
      }

      private async void ApplyCommited()
      {
         for (; lastApplied < commitIndex; lastApplied++)
         {
            await Task.Run(() => internalState.Apply(entriesLog[lastApplied + 1]));
         }
      }

      /// <summary>
      /// Change term if new term is larger.
      /// </summary>
      /// <param name="Term">New term</param>
      /// <param name="possibleLeaderId">Node's id with new term</param>
      /// <returns>
      /// False if new term isn't larger, didn't convert to follower. 
      /// True if new term is larger, so node was coverted to follower. 
      /// </returns>
      private bool TryBecomingFollower(int Term, int possibleLeaderId)
      {
         if (Term <= CurrentTerm)
         {
            return false;
         }

         CurrentTerm = Term;
         switch (nodeState)
         {
            case State.Follower:
               _logger.LogInformation("Receive AppendEntries from node with bigger Term as Follower's. New Leader");
               break;
            case State.Candidate:
               _logger.LogInformation("Receive AppendEntries from node with at least as large Term as Candidate's. New Leader. Becoming follower");
               _logger.LogInformation("Premature election stop");
               StopElection();
               break;
            case State.Leader:
               _logger.LogInformation("Receive AppendEntries from node with bigger Term as Leader's. New Leader. Becoming follower");
               break;
         }
         BecomeFollower(possibleLeaderId);

         return true;
      }

      private bool IsNewLogBetter(int NewLogIndex, int NewLogTerm)
      {
         return commitTerm <= NewLogTerm && commitIndex <= NewLogIndex;
      }

      private async void OnBroadcastElapsed(object? Ignored)
      {
         _logger.LogTrace($"Broadcast timer elapsed");

         // Heartbeat
         List<Task> taskList = [];
         AppendEntriesRequest<TDataIn> request;
         foreach (int nodeId in peersStatus.Keys)
         {
            request = new AppendEntriesRequest<TDataIn>(CurrentTerm, id, commitIndex, commitTerm, [], commitIndex);

            taskList.Add(TryHeatbeat(nodeId, request, _globalCancel.Token));
         }
         if (_globalCancel.IsCancellationRequested)
         {
            return;
         }

         await Task.WhenAll(taskList);
      }

      private async Task TryHeatbeat(int receiverId, AppendEntriesRequest<TDataIn> request, CancellationToken cancellationToken)
      {
         try
         {
            var requestTask = SendHeartbeat(receiverId, request, cancellationToken);
            await requestTask.ContinueWith(
               (task) => HandleHeartbeatReply(task.Result),
               cancellationToken
            );

            if (peersStatus[receiverId] == false)
            {
               _logger.LogWarning("Previously inactive {id} received Heartbeat. Marking it as active", receiverId);
            }
            peersStatus[receiverId] = true; // mark receiver as active if everything completes
         }
         catch (Exception e)
         {
            if (peersStatus[receiverId]) // if exception persists -> don't do repeated steps
            {
               _logger.LogWarning("Couldn't send Heartbeat to {id}. Marking it as inactive", receiverId);
               _logger.LogTrace("Connection error message: {message}", e.Message);
               peersStatus[receiverId] = false; // mark receiver as inactive
            }
         }
      }

      private void ResetElectionTimer()
      {
         electionTimer.Change(GetRandomElectionTime(), Timeout.Infinite);
      }

      private async Task BeginElection(CancellationToken cancellationToken)
      {
         CurrentTerm++;

         _logger.LogInformation("Begin election for Term {Term}", CurrentTerm);

         VotedFor = id;
         votesGot = 1;

         List<Task> taskList = [];
         VoteRequest request;
         foreach (int peerId in peersStatus.Keys)
         {
            request = new VoteRequest(CurrentTerm, id, commitIndex, commitTerm);

            taskList.Add(TryVoteRequest(peerId, request, cancellationToken));
         }
         if (cancellationToken.IsCancellationRequested)
         {
            return;
         }

         await Task.WhenAll(taskList);
         _logger.LogInformation("Received all VoteRequest replies from {num} nodes", ActiveNodesNumber);

         if (votesGot >= _votesRequired)
         {
            BecomeLeader();
         }
         else
         {
            _logger.LogInformation("Not enough votes received ({votesGot} / {votesNeeded})", votesGot, _votesRequired);
         }
      }

      private async Task TryVoteRequest(int receiverId, VoteRequest request, CancellationToken cancellationToken)
      {
         try
         {
            var requestTask = SendRequestVote(receiverId, request, cancellationToken);
            await requestTask.ContinueWith((task) =>
            {
               HandleRequestVoteReply(task.Result);
            },
            cancellationToken);

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

      private void OnElectionElapsed(object? Ignored)
      {
         _logger.LogTrace("Election timer elapsed");

         ResetElectionTimer();
         StopElection();

         _currentElectionCancellation = new CancellationTokenSource();
         var savedToken = _currentElectionCancellation;

         lock (_electionLock)
         {
            if (nodeState == State.Leader)
            {
               _logger.LogTrace("Node is Leader. Election doesn't begin");
               return;
            }

            if (!Voted || VotedFor == id)
            {
               BecomeCandidate(savedToken.Token);
               return;
            }

            _logger.LogTrace("Node has voted. Election doesn't begin");
            Voted = false;
         }
      }

      private void BecomeLeader()
      {
         _logger.LogInformation("Become leader with {got} / {required} votes", votesGot, ActiveNodesNumber);
         broadcastTimer.Change(0, broadcastTimeout);
         nodeState = State.Leader;
         //TODO: init next index
         //TODO: init match index
      }

      private Task BecomeCandidate(CancellationToken cancellationToken)
      {
         StopBroadcast();
         nodeState = State.Candidate;
         return BeginElection(cancellationToken);
      }

      private void BecomeFollower(int newLeaderId)
      {
         _logger.LogInformation("New Leader is {id}", newLeaderId);
         leaderId = newLeaderId;
         ResetElectionTimer();
         StopBroadcast();
         StopElection();
         nodeState = State.Follower;
      }

      private void StopBroadcast()
      {
         bool result = broadcastTimer.Change(Timeout.Infinite, broadcastTimeout);
         if (!result)
         {
            throw new Exception($"Node #{id} couldn't denote from leadership");
         }
      }
      private void StopElection()
      {
         _currentElectionCancellation.Cancel();
      }

      private RaftLogEntry<TDataIn> CreateLogEntry(TDataIn Data)
      {
         return new RaftLogEntry<TDataIn>(commitIndex, CurrentTerm, Data);
      }


      private int GetRandomElectionTime()
      {
         return _random.Next(minElectionTimeout, maxElectionTimeout);
      }
   }
}