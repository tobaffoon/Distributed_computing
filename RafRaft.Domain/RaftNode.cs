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
      private readonly IRaftMediator<TDataIn> _mediator;
      private readonly TState internalState;
      private readonly Dictionary<int, bool> peersStatus;
      private readonly ILogger _logger;

      private State nodeState = State.Follower;
      private int currentTerm = 0;
      private int? leaderId = null;

      private int? _votedFor = null;
      private int? VotedFor
      {
         get => _votedFor;
         set
         {
            _votedFor = value;
         }
      }
      private int VotesRequired => (peersStatus.Count + 1) / 2 + 1;
      private int votesGot = 0;
      private int ActiveNodesNumber => peersStatus.Where(kvPair => kvPair.Value).Count() + 1; // where peerStatus is true (peer is active)

      private readonly List<RaftLogEntry<TDataIn>> log = [new RaftLogEntry<TDataIn>(0, 0, default)]; // sentinel entry to retreive info for regular append entries
      private int commitIndex = 0;
      private int LastLogIndex => log.Count - 1;
      private int LastLogTerm => log[^1].Term;
      private int lastApplied = 0;
      private Dictionary<int, int> nextIndex = [];
      private Dictionary<int, int> matchIndex = [];

      private readonly long broadcastTimeout;
      private readonly Timer broadcastTimer;
      private readonly int minElectionTimeout;
      private readonly int maxElectionTimeout;
      private readonly Timer electionTimer;
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

         _mediator = mediator;
         _logger = logger;
      }

      #region AppendEntries
      public AppendEntriesReply HandleAppendEntriesRequest(AppendEntriesRequest<TDataIn> request)
      {
         _logger.LogInformation("Received AppendEntries request from {id}", request.LeaderId);

         if (request.LeaderId == leaderId)
         {
            RestartElectionTimer();
         }

         #region Term correction
         bool isNewTermLarger = TryBecomingFollower(request.Term, request.LeaderId);
         if (!isNewTermLarger)
         {
            return new AppendEntriesReply(currentTerm, false);
         }
         #endregion

         #region Previous log entry discovery
         RaftLogEntry<TDataIn>? prevEntry = log[request.PrevLogId];
         if (prevEntry is null || prevEntry.Term != request.PrevLogTerm)
         {
            return new AppendEntriesReply(currentTerm, false);
         }
         #endregion

         log.AddRange(request.Entries);

         /* if (request.LeaderId > commitIndex)
         {
            commitIndex = Math.Min(request.LeaderCommitId, lastNewEntryId);
         } */

         ApplyCommited();

         return new AppendEntriesReply(currentTerm, true);
      }

      private Task<AppendEntriesReply> SendAppendEntries(
         int receiverId,
         AppendEntriesRequest<TDataIn> request)
      {
         _logger.LogInformation("Send AppendEntries request to {id}", receiverId);
         return _mediator.SendAppendEntries(receiverId, request);
      }

      private void HandleAppendEntriesReply(AppendEntriesReply reply)
      {
         // TODO logic
      }
      #endregion

      #region RequestVote
      public VoteReply HandleRequestVoteRequest(VoteRequest request)
      {
         _logger.LogInformation("Received RequestVote request from {id} for Term {term}", request.CandidateId, request.Term);

         bool isNewTermLarger = TryBecomingFollower(request.Term, request.CandidateId);
         if (isNewTermLarger)
         {
            _logger.LogInformation("Vote granted to {candidateId}, because its Term is higher", request.CandidateId);
            return new VoteReply(currentTerm, true);
         }

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
         return new VoteReply(currentTerm, true);
      }

      private Task<VoteReply> SendRequestVote(int receiverId, VoteRequest request)
      {
         _logger.LogInformation("Send RequestVote request to {id}", receiverId);

         return _mediator.SendRequestVote(receiverId, request);
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
         RestartElectionTimer();
      }

      private async void ApplyCommited()
      {
         for (; lastApplied < commitIndex; lastApplied++)
         {
            await Task.Run(() => internalState.Apply(log[lastApplied + 1]));
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
      private bool TryBecomingFollower(int term, int possibleLeaderId)
      {
         if (term <= currentTerm)
         {
            return false;
         }

         currentTerm = term;
         switch (nodeState)
         {
            case State.Follower:
               _logger.LogInformation("Receive AppendEntries from node with bigger Term as Follower's.");
               break;
            case State.Candidate:
               _logger.LogInformation("Receive AppendEntries from node with at least as large Term as Candidate's. Becoming follower");
               break;
            case State.Leader:
               _logger.LogInformation("Receive AppendEntries from node with bigger Term as Leader's. Becoming follower");
               break;
         }
         BecomeFollower(possibleLeaderId);

         return true;
      }

      private bool IsLogWorse(int otherLogIndex, int otherLogTerm)
      {
         return otherLogTerm < LastLogTerm && otherLogIndex < LastLogIndex;
      }

      private async void OnBroadcastElapsed(object? Ignored)
      {
         _logger.LogTrace($"Broadcast timer elapsed");

         List<Task> taskList = [];
         AppendEntriesRequest<TDataIn> request;
         foreach (int nodeId in peersStatus.Keys)
         {
            request = new AppendEntriesRequest<TDataIn>(currentTerm, id, 0, 0, [], commitIndex);

            taskList.Add(AppendEntries(nodeId, request));
         }

         await Task.WhenAll(taskList);
      }

      private async Task AppendEntries(int receiverId, AppendEntriesRequest<TDataIn> request)
      {
         try
         {
            var requestTask = SendAppendEntries(receiverId, request);
            await requestTask.ContinueWith(
               (task) => HandleAppendEntriesReply(task.Result)
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

      private void RestartElectionTimer()
      {
         electionTimer.Change(GetRandomElectionTime(), Timeout.Infinite);
      }

      private async Task BeginElection()
      {
         currentTerm++;

         _logger.LogInformation("Begin election for Term {Term}", currentTerm);

         VotedFor = id;
         votesGot = 1;

         List<Task> taskList = [];
         VoteRequest request;
         foreach (int peerId in peersStatus.Keys)
         {
            request = new VoteRequest(currentTerm, id, 0, 0);

            taskList.Add(TryVoteRequest(peerId, request));
         }

         await Task.WhenAll(taskList);

         // leader was found during election
         if (nodeState != State.Candidate)
         {
            return;
         }
         _logger.LogInformation("Received all VoteRequest replies from {num} nodes", ActiveNodesNumber);

         if (votesGot >= VotesRequired)
         {
            BecomeLeader();
         }
         else
         {
            _logger.LogInformation("Not enough votes received ({votesGot} / {votesNeeded})", votesGot, VotesRequired);
         }
      }

      private async Task TryVoteRequest(int receiverId, VoteRequest request)
      {
         try
         {
            var requestTask = SendRequestVote(receiverId, request);
            await requestTask.ContinueWith((task) =>
            {
               HandleRequestVoteReply(task.Result);
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

      private async void OnElectionElapsed(object? Ignored)
      {
         _logger.LogTrace("Election timer elapsed");

         if (nodeState == State.Candidate)
         {
            RestartElectionTimer();
            await BeginElection();
            return;
         }

         if (VotedFor == null)
         {
            RestartElectionTimer();
            await BecomeCandidate();
            return;
         }

         _logger.LogTrace("Node has voted. Election doesn't begin");
      }

      private void BecomeLeader()
      {
         _logger.LogInformation("Become leader with {got} / {required} votes", votesGot, ActiveNodesNumber);
         broadcastTimer.Change(0, broadcastTimeout); // start broadcast
         electionTimer.Change(Timeout.Infinite, Timeout.Infinite); // stop election timer
         VotedFor = null;
         nodeState = State.Leader;
         //TODO: init next index
         //TODO: init match index
      }

      private async Task BecomeCandidate()
      {
         broadcastTimer.Change(Timeout.Infinite, broadcastTimeout);
         nodeState = State.Candidate;
         await BeginElection();
      }

      private void BecomeFollower(int newLeaderId)
      {
         _logger.LogInformation("New Leader is {id}", newLeaderId);
         leaderId = newLeaderId;
         VotedFor = null;
         RestartElectionTimer();
         broadcastTimer.Change(Timeout.Infinite, broadcastTimeout);
         nodeState = State.Follower;
      }

      private RaftLogEntry<TDataIn> CreateLogEntry(TDataIn Data)
      {
         return new RaftLogEntry<TDataIn>(commitIndex, currentTerm, Data);
      }


      private int GetRandomElectionTime()
      {
         return _random.Next(minElectionTimeout, maxElectionTimeout);
      }
   }
}