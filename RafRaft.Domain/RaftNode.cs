namespace RafRaft.Domain
{
   using RafRaft.Domain.Messages;
   using Microsoft.Extensions.Logging;

   public class RaftNode<TState, TDataIn, TDataOut>
     where TState : IRaftStateMachine<TDataIn, TDataOut>, new()
     where TDataIn : notnull
     where TDataOut : notnull
   {
      public enum State
      {
         Follower,
         Candidate,
         Leader,
         Stopped
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
      private readonly long electionTimeout;
      private readonly Timer electionTimer;
      private List<int> nodeIds;
      private int ClusterSize => nodeIds.Count + 1;
      private int leaderId;
      private int votesGot = 0;
      private int _heartbeatRecievedBackValue = 0;

      private bool HeartbeatRecieved
      {
         get { return Interlocked.CompareExchange(ref _heartbeatRecievedBackValue, 1, 1) == 1; }
         set
         {
            if (value) Interlocked.CompareExchange(ref _heartbeatRecievedBackValue, 1, 0);
            else Interlocked.CompareExchange(ref _heartbeatRecievedBackValue, 0, 1);
         }
      }

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
      private CancellationTokenSource _currentAppendEntriesCancellation = new CancellationTokenSource();
      private ILogger _logger;

      public RaftNode(RaftNodeConfig config, IRaftMediator<TDataIn> mediator, ILogger logger)
      {
         broadcastTimeout = config.BroadcastTime;
         electionTimeout = config.ElectionTimeout;

         broadcastTimer = new Timer(OnBroadcastElapsed, null, Timeout.Infinite, broadcastTimeout);
         electionTimer = new Timer(OnElectionElapsed, null, Timeout.Infinite, electionTimeout);

         internalState = new TState();

         id = config.Id;

         var nodesWithoutThis = config.NodeIds.Except([id]);
         nodeIds = new List<int>(nodesWithoutThis);

         this.mediator = mediator;

         nextIndex = new List<int>(nodeIds.Count);
         matchIndex = new List<int>(nodeIds.Count);

         entriesLog = new List<RaftLogEntry<TDataIn>>();

         _logger = logger;
      }

      #region Heartbeat
      public AppendEntriesReply HandleHeartbeatRequest(AppendEntriesRequest<TDataIn> request)
      {
         HeartbeatRecieved = true;

         if (nodeState == State.Candidate)
         {
            StopElection();
         }
         CorrectLeader(request.LeaderId);

         return new AppendEntriesReply(CurrentTerm, true);
      }

      private Task<AppendEntriesReply> SendHeartbeat(
         int receiverId,
         AppendEntriesRequest<TDataIn> request,
         CancellationToken token)
      {
         return mediator.SendHeartbeat(receiverId, request, token);
      }

      private void HandleHeartbeatReply(AppendEntriesReply reply)
      {
         CorrectTerm(reply.Term);
      }
      #endregion

      #region AppendEntries
      public AppendEntriesReply HandleAppendEntriesRequest(AppendEntriesRequest<TDataIn> request)
      {
         HeartbeatRecieved = true;

         #region Term correction
         if (request.Term < CurrentTerm)
         {
            return new AppendEntriesReply(CurrentTerm, false);
         }
         CorrectTerm(request.Term);
         #endregion

         if (nodeState == State.Candidate)
         {
            StopElection();
         }
         CorrectLeader(request.LeaderId);

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
         return mediator.SendAppendEntries(receiverId, request, token);
      }

      private void HandleAppendEntriesReply(AppendEntriesReply reply)
      {
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
         if (request.Term < CurrentTerm)
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because Term is too low", request.CandidateId);
            return new VoteReply(CurrentTerm, false);
         }
         CorrectTerm(request.Term);

         if (Voted)
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because Node alreay voted", request.CandidateId);
            return new VoteReply(CurrentTerm, false);
         }
         if (!IsNewLogBetter(request.LastLogId, request.LastLogTerm))
         {
            _logger.LogInformation("Vote not granted to {candidateId}, because its log is worse", request.CandidateId);
            return new VoteReply(CurrentTerm, false);
         }

         _logger.LogInformation("Vote granted to {candidateId}", request.CandidateId);
         VotedFor = request.CandidateId;
         return new VoteReply(CurrentTerm, true);
      }

      private Task<VoteReply> SendRequestVote(int receiverId, VoteRequest request, CancellationToken token)
      {
         return mediator.SendRequestVote(receiverId, request, token);
      }

      private void HandleRequestVoteReply(VoteReply reply)
      {
         CorrectTerm(reply.Term);
         if (nodeState == State.Candidate && reply.VoteGranted) votesGot++;
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
            await Task.CompletedTask;
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
         electionTimer.Change(0, electionTimeout);
      }

      private async void ApplyCommited()
      {
         for (; lastApplied < commitIndex; lastApplied++)
         {
            await Task.Run(() => internalState.Apply(entriesLog[lastApplied + 1]));
         }
      }

      private void CorrectTerm(int Term)
      {
         if ((nodeState == State.Candidate || nodeState == State.Leader) && Term > CurrentTerm)
         {
            _logger.LogInformation("Request from node with bigger Term. Becoming follower");
            CurrentTerm = Term;
            BecomeFollower();
         }
      }

      private void CorrectLeader(int LeaderId)
      {
         leaderId = LeaderId;
      }

      private bool IsNewLogBetter(int NewLogIndex, int NewLogTerm)
      {
         return commitTerm <= NewLogTerm && commitIndex <= NewLogIndex;
      }

      private async void OnBroadcastElapsed(object? Ignored)
      {
         _logger.LogTrace($"Broadcast timer elapsed");

         _currentAppendEntriesCancellation = new CancellationTokenSource();
         var savedToken = _currentAppendEntriesCancellation;

         // Heartbeat
         List<Task> taskList = [];
         AppendEntriesRequest<TDataIn> request;
         foreach (int nodeId in nodeIds)
         {
            // TODO add cancelettion token for downgrading to follower case
            // TODO add failed heartbeat (dead node) handling
            request = new AppendEntriesRequest<TDataIn>(CurrentTerm, id, commitIndex, commitTerm, [], commitIndex);

            var requestTask = SendHeartbeat(nodeId, request, savedToken.Token);
            var replyTask = requestTask.ContinueWith((task) =>
               HandleHeartbeatReply(task.Result));

            taskList.Add(requestTask);
            taskList.Add(replyTask);
         }

         await Task.WhenAll(taskList);
      }

      private async void BeginElection(CancellationToken cancellationToken)
      {
         CurrentTerm++;

         VotedFor = id;
         votesGot = 1;

         List<Task> taskList = [];
         VoteRequest request;
         foreach (int nodeId in nodeIds)
         {
            request = new VoteRequest(CurrentTerm, id, commitIndex, commitTerm);
            var voteTask = SendRequestVote(nodeId, request, cancellationToken);
            var replyTask = voteTask.ContinueWith((task) => HandleRequestVoteReply(task.Result));
            taskList.Add(voteTask);
            taskList.Add(replyTask);
         }
         if (cancellationToken.IsCancellationRequested)
         {
            return;
         }
         await Task.WhenAll(taskList);
         _logger.LogInformation("Received all VoteRequest replies from peers");

         if (VotesAreEnough())
         {
            BecomeLeader();
         }
      }

      private void OnElectionElapsed(object? Ignored)
      {
         _logger.LogTrace("Election timer elapsed");

         _currentElectionCancellation.Cancel(); // cancel current election
         _currentElectionCancellation = new CancellationTokenSource();
         var savedToken = _currentElectionCancellation;

         if (nodeState == State.Leader)
         {
            _logger.LogTrace("Node is Leader. Election doesn't begin");
            return;
         }

         if (!HeartbeatRecieved && (!Voted || VotedFor == id))
         {
            _logger.LogInformation("Begin election");
            BecomeCandidate(savedToken.Token);
            return;
         }

         _logger.LogTrace("Node has Leader or voted. Election doesn't begin");
         Voted = false;
         HeartbeatRecieved = false;
      }

      private void BecomeLeader()
      {
         _logger.LogInformation("Become leader");
         broadcastTimer.Change(0, broadcastTimeout);
         nodeState = State.Leader;
         //TODO: init next index
         //TODO: init match index
      }

      private void BecomeCandidate(CancellationToken cancellationToken)
      {
         StopBroadcast();
         nodeState = State.Candidate;
         BeginElection(cancellationToken);
      }

      private void BecomeFollower()
      {
         _logger.LogInformation("New Leader is Node #{id}", leaderId);
         StopBroadcast();
         _currentElectionCancellation.Cancel();
         nodeState = State.Follower;
      }

      private void StopBroadcast()
      {
         bool result = broadcastTimer.Change(Timeout.Infinite, broadcastTimeout); // or both Infinte?
         if (!result)
         {
            throw new Exception($"Node #{id} couldn't denote from leadership");
         }
      }

      private bool VotesAreEnough()
      {
         return votesGot * 2 > ClusterSize;
      }

      private void StopElection()
      {
         _logger.LogInformation("Premature election stop");
         _currentElectionCancellation.Cancel();
         nodeState = State.Follower;
      }

      private RaftLogEntry<TDataIn> CreateLogEntry(TDataIn Data)
      {
         return new RaftLogEntry<TDataIn>(commitIndex, CurrentTerm, Data);
      }

   }
}