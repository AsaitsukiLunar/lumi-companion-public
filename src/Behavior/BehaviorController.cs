/*
Traceability metadata
Version: 0.3.21-preview
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-11-17
Content coverage through: 2026-11-17
Source type: RAW | user agent separation and shared confirmation rules; LOCAL-WORK | Lumi behavior arbitration
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
Organizing node: Windows / Codex Desktop / WorkBuddy snapshot behavior subtask
Original sources: existing Lumi task behavior and WorkBuddy bridge handoff V1.0
*/

using LumiCompanion.Animation;
using LumiCompanion.Integration;
using LumiCompanion.Models;
using LumiCompanion.Sensors;

namespace LumiCompanion.Behavior;

public sealed class VisualStateChangedEventArgs : EventArgs
{
    public AnimationId Animation { get; init; }
    public int? LookIndex { get; init; }
    public string Hint { get; init; } = string.Empty;
    public bool? LoopOverride { get; init; }
    public int? StartFrameOverride { get; init; }
    public int? EndFrameOverride { get; init; }
}

public sealed class BehaviorController
{
    private static readonly ConversationProgress EmptyBatch = ConversationProgress.FromStates([], currentBatch: true);
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private LumiSettings _settings;
    private AnimationId _currentAnimation = AnimationId.Idle;
    private int? _currentLookIndex;
    private string _currentHint = "待机";
    private bool? _currentLoopOverride;
    private int? _currentStartFrameOverride;
    private int? _currentEndFrameOverride;
    private readonly TaskStateArbiter _sourceArbiter = new();
    private readonly Dictionary<(string Source, string TaskId), ObservedTask> _taskObservations = new();
    private readonly Dictionary<(string Source, string TaskId), TaskHistory> _taskHistory = new();
    private readonly HashSet<TaskFingerprint> _acknowledgedTasks = new();
    private readonly Dictionary<(string Source, string TaskId, string Turn), ObservedTask> _terminalResults = new();
    private readonly Dictionary<(string Source, string TaskId), AgentTaskSnapshot> _conversationBatch = new();
    private bool _bodyHovered;
    private bool _dragging;
    private readonly DragMotionTracker _dragMotion = new();
    private bool _dragAcknowledgedTerminal;
    private int _reactionPriority;
    private string? _reactionKey;
    private bool _reactionAcknowledgesTerminal;
    private DateTimeOffset _reactionMinimumUntil;
    private DateTimeOffset _reactionEndsAt;
    private double _idleSeconds;
    private double _previousIdleSeconds;
    private bool _locked;
    private bool _audioActive;
    private int? _lookIndex;
    private DateTimeOffset _lastRandomIdle;

    public BehaviorController(LumiSettings settings, TimeProvider? timeProvider = null)
    {
        _settings = settings;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastRandomIdle = _timeProvider.GetUtcNow();
    }

    public event EventHandler<VisualStateChangedEventArgs>? VisualStateChanged;

    public AnimationId CurrentAnimation => _currentAnimation;
    public bool? CurrentLoopOverride => _currentLoopOverride;
    public int? CurrentStartFrameOverride => _currentStartFrameOverride;
    public int? CurrentEndFrameOverride => _currentEndFrameOverride;
    /// <summary>Explicit click or real-drag boundary, not hover or a polling/read timestamp.</summary>
    public DateTimeOffset? LastAcknowledgementAt { get; private set; }

    /// <summary>Effective task status, independent of temporary pointer-driven animation.</summary>
    public AggregateTaskState GetTaskState() => ReadTaskState(_timeProvider.GetUtcNow());

    /// <summary>Read-only status input. Source versions must identify events, not lease refresh times.</summary>
    public bool ObserveTaskState(AgentTaskSnapshot snapshot, string? stateVersion = null, bool announceTerminal = true,
        string? turnId = null)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!_sourceArbiter.Observe(snapshot, now)) return false;
        var key = (snapshot.Source, snapshot.TaskId);
        _taskHistory.TryGetValue(key, out TaskHistory? previous);
        long generation = previous is null ? 1 : previous.Generation + (previous.State != snapshot.State ? 1 : 0);
        string version = !string.IsNullOrWhiteSpace(stateVersion) ? "source:" + stateVersion :
            previous is not null && previous.State == snapshot.State ? previous.Version : "local:" + generation;
        var fingerprint = new TaskFingerprint(snapshot.Source, snapshot.TaskId, snapshot.State,
            IsTerminal(snapshot.State) && !string.IsNullOrWhiteSpace(turnId) ? "turn:" + turnId : version);
        _taskHistory[key] = new TaskHistory(snapshot.State, version, generation);
        _taskObservations[key] = new ObservedTask(snapshot, fingerprint);
        bool revisesKnownTurn = !IsTerminal(snapshot.State) && !string.IsNullOrWhiteSpace(turnId) &&
            _terminalResults.Remove((snapshot.Source, snapshot.TaskId, "turn:" + turnId));
        if (IsTerminal(snapshot.State))
        {
            _conversationBatch.Remove(key);
            ObserveTerminalResult(new(snapshot, fingerprint), announceTerminal);
        }
        else if (revisesKnownTurn || _conversationBatch.ContainsKey(key) || IsActiveTaskState(snapshot.State) ||
            _terminalResults.Keys.Any(result => result.Source == snapshot.Source && result.TaskId == snapshot.TaskId))
            _conversationBatch[key] = snapshot;
        if (!IsDirectReaction(_reactionKey)) ClearReaction();
        Tick();
        return true;
    }

    /// <summary>Receives verified completed turns separately from a chat's latest live state.</summary>
    public void ObserveTaskCompletion(ObservedCompletionEvent completion, bool announceTerminal = true)
    {
        if (string.IsNullOrWhiteSpace(completion.Source) || string.IsNullOrWhiteSpace(completion.TaskId) ||
            string.IsNullOrWhiteSpace(completion.TurnId)) return;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        var snapshot = new AgentTaskSnapshot(completion.Source, completion.TaskId, AgentTaskState.Completed,
            now, now.AddSeconds(15));
        var fingerprint = new TaskFingerprint(completion.Source, completion.TaskId,
            AgentTaskState.Completed, "turn:" + completion.TurnId);
        ObserveTerminalResult(new(snapshot, fingerprint), announceTerminal);
        if (!IsDirectReaction(_reactionKey)) ClearReaction();
        Tick();
    }

    private void ObserveTerminalResult(ObservedTask task, bool announceTerminal)
    {
        if (!announceTerminal) _acknowledgedTasks.Add(task.Fingerprint);
        var key = (task.Snapshot.Source, task.Snapshot.TaskId, task.Fingerprint.Version);
        // Old startup events and events already confirmed before delivery cannot start a batch.
        // Confirmed results already in this batch remain counted while live work continues.
        if (_terminalResults.ContainsKey(key) || !_acknowledgedTasks.Contains(task.Fingerprint))
            _terminalResults[key] = task;
    }

    public void DisconnectTaskSource(string source)
    {
        _sourceArbiter.Disconnect(source);
        foreach (var key in _taskObservations.Keys.Where(key => key.Source == source).ToArray())
            _taskObservations.Remove(key);
        foreach (var key in _conversationBatch.Keys.Where(key => key.Source == source).ToArray())
            _conversationBatch.Remove(key);
        foreach (var key in _terminalResults.Keys.Where(key => key.Source == source).ToArray())
            _terminalResults.Remove(key);
        // Retain event fingerprints so reconnecting the same acknowledged event cannot re-alert.
        Tick();
    }

    /// <summary>Silently acknowledges current terminal observations; useful for startup/account baselines.</summary>
    public void AcknowledgeTaskNotifications()
    {
        AcknowledgeCurrentTerminals();
        Tick();
    }

    public void SetBodyHovered(bool hovered)
    {
        if (_bodyHovered == hovered) return;
        _bodyHovered = hovered;
        if (hovered && !IsDirectReaction(_reactionKey)) ClearReaction();
        Tick();
    }

    public void SetDragging(bool dragging)
    {
        if (_dragging == dragging) return;
        _dragging = dragging;
        _dragMotion.Reset();
        ClearReaction();
        LastAcknowledgementAt = _timeProvider.GetUtcNow();
        if (dragging)
        {
            _dragAcknowledgedTerminal = AcknowledgeCurrentTerminals();
        }
        else
        {
            bool acknowledgedNow = AcknowledgeCurrentTerminals();
            bool acknowledged = _dragAcknowledgedTerminal || acknowledgedNow;
            _dragAcknowledgedTerminal = false;
            // Never wave in the middle of dragging, or hide still-active work on release.
            if (acknowledged && !IsActiveTaskState(ReadTaskState(_timeProvider.GetUtcNow()).State))
            {
                RequestReaction(AnimationId.Waving, 100, 1.8, 1.0, "direct-wave", 0.4,
                    "知道啦，继续陪你", bypassCooldown: true, acknowledgesTerminal: true);
                return;
            }
        }
        Tick();
    }

    public void HandleDragMotion(double deltaX, double deltaY)
    {
        if (!_dragging) return;
        _dragMotion.Observe(deltaX, deltaY, _timeProvider.GetUtcNow());
        Tick();
    }

    public void UpdateSettings(LumiSettings settings)
    {
        _settings = settings;
    }

    public void HandleInput(InputActivitySnapshot input)
    {
        _previousIdleSeconds = _idleSeconds;
        _idleSeconds = input.IdleSeconds;

        if (_settings.SensorsPaused || !_settings.MonitorInputActivity)
        {
            return;
        }

        bool resumed = input.ActivitySinceLastSample && _previousIdleSeconds >= 30 && _idleSeconds < 2;
        if (resumed)
        {
            RequestReaction(AnimationId.Waving, 80, 1.7, 0.8, "wake", 8, "欢迎回来");
            return;
        }

        if (input.KeyboardEvents >= 4)
        {
            RequestReaction(AnimationId.Running, 30, 1.25, 0.65, "keyboard-active", 8, "检测到键盘活动（不记录按键）");
        }
        else if (input.MouseEvents >= 5 && !HasActiveGaze)
        {
            RequestReaction(AnimationId.Review, 25, 1.1, 0.55, "mouse-active", 8, "检测到鼠标活动（不记录坐标）");
        }
    }

    public void HandleWindow(WindowActivity window, bool opened)
    {
        if (_settings.SensorsPaused || !_settings.MonitorWindows)
        {
            return;
        }

        string category = CategorizeWindow(window.ProcessName, window.Title);
        if (opened)
        {
            RequestReaction(AnimationId.Waving, 70, 1.6, 0.8, "window-open:" + window.ProcessName, 6, "注意到新窗口");
            return;
        }

        switch (category)
        {
            case "communication":
                RequestReaction(AnimationId.Waving, 68, 1.6, 0.8, "foreground-communication", 8, "切换到沟通窗口");
                break;
            case "media":
                RequestReaction(AnimationId.Jumping, 60, 1.8, 0.9, "foreground-media", 12, "注意到媒体窗口");
                break;
            case "development":
                RequestReaction(AnimationId.Running, 65, 1.7, 0.85, "foreground-development", 8, "进入工作状态");
                break;
            case "document":
            case "browser":
                RequestReaction(AnimationId.Review, 62, 1.6, 0.8, "foreground-review", 8, "专注查看窗口");
                break;
            default:
                RequestReaction(AnimationId.Review, 55, 1.2, 0.6, "foreground-other", 10, "注意到窗口切换");
                break;
        }
    }

    public void HandleAudioChanged(bool active)
    {
        bool started = active && !_audioActive;
        _audioActive = active;
        if (started && !_settings.SensorsPaused && _settings.MonitorAudio)
        {
            RequestReaction(AnimationId.Jumping, 60, 2.4, 1.0, "audio-start", 30, "检测到声音播放（不采集音频）");
        }
    }

    public void HandleDirectInteraction(bool energetic = false)
    {
        if (_dragging) return;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        LastAcknowledgementAt = now;
        bool acknowledged = AcknowledgeCurrentTerminals();
        // WPF delivers a double click as first-up(false), then second-down(true).
        // The first callback may already have acknowledged the terminal: preserve
        // that one wave instead of replacing its feedback with an ordinary jump.
        if (energetic && !acknowledged && _reactionAcknowledgesTerminal &&
            _reactionKey == "direct-wave" && _reactionPriority > 0 && now < _reactionEndsAt)
            return;
        RequestReaction(
            energetic && !acknowledged ? AnimationId.Jumping : AnimationId.Waving,
            acknowledged ? 100 : 85,
            energetic && !acknowledged ? 2.1 : 1.8,
            1.0,
            energetic && !acknowledged ? "direct-jump" : "direct-wave",
            0.4,
            acknowledged ? "知道啦，继续陪你" : energetic ? "开心" : "你好", bypassCooldown: acknowledged,
            acknowledgesTerminal: acknowledged);
    }

    public void HandleDragging(bool rightward)
    {
        // Legacy notification is informational; SetDragging owns the sustained airborne state.
        // In particular, a late drag-end notification must not restart a running animation.
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        if (!locked)
        {
            RequestReaction(AnimationId.Waving, 80, 1.7, 0.8, "unlock", 3, "欢迎回来");
        }
    }

    public void SetLookDirection(int? index)
    {
        _lookIndex = index;
    }

    private bool HasActiveGaze => _settings.MouseLook && _lookIndex.HasValue &&
                                  _idleSeconds < _settings.DrowsyAfterSeconds;

    public void HandleAnimationCompleted(AnimationId animation)
    {
        // Ignore late completions from a visual that has already been replaced.
        if (_currentAnimation != animation || _reactionPriority == 0) return;
        ClearReaction();
        Tick();
    }

    public void Tick()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_locked)
        {
            return;
        }

        if (_dragging)
        {
            int direction = _dragMotion.ReadDirection(now);
            if (direction != 0)
                SetVisual(direction < 0 ? AnimationId.RunningLeft : AnimationId.RunningRight, null,
                    direction < 0 ? "正在被向左拖动" : "正在被向右拖动", loopOverride: true);
            else
                SetVisual(AnimationId.Jumping, null, "被提起来了", loopOverride: false,
                    startFrameOverride: 2, endFrameOverride: 2);
            return;
        }

        if (_reactionPriority > 0 && (now >= _reactionEndsAt ||
            (_reactionKey == "mouse-active" && HasActiveGaze)))
        {
            ClearReaction();
        }

        if (_reactionPriority > 0)
        {
            return;
        }

        if (_bodyHovered)
        {
            SetVisual(HasActiveGaze ? AnimationId.Look : AnimationId.Idle,
                HasActiveGaze ? _lookIndex : null, "正在和你互动");
            return;
        }

        if (TryShowTaskState(ReadTaskState(now))) return;

        if (HasActiveGaze)
        {
            SetVisual(AnimationId.Look, _lookIndex!.Value, "看向鼠标");
            return;
        }

        if (_idleSeconds >= _settings.SleepAfterSeconds)
        {
            SetVisual(AnimationId.Idle, null, "安静休息");
            return;
        }

        if (_idleSeconds >= _settings.DrowsyAfterSeconds)
        {
            SetVisual(AnimationId.Waiting, null, "等待你回来");
            return;
        }

        if (now - _lastRandomIdle > TimeSpan.FromSeconds(50))
        {
            _lastRandomIdle = now;
            RequestReaction(AnimationId.Waving, 10, 1.5, 0.7, "random-wave", 45, "偶尔打个招呼");
            return;
        }

        SetVisual(AnimationId.Idle, null, _audioActive ? "安静陪你听" : "待机");
    }

    private bool RequestReaction(
        AnimationId animation,
        int priority,
        double durationSeconds,
        double minimumSeconds,
        string cooldownKey,
        double cooldownSeconds,
        string hint,
        bool bypassCooldown = false,
        bool acknowledgesTerminal = false)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_dragging || (!IsDirectReaction(cooldownKey) &&
            (_bodyHovered || HasTaskVisual(ReadTaskState(now).State)))) return false;
        if (!bypassCooldown && _cooldowns.TryGetValue(cooldownKey, out DateTimeOffset last) && now - last < TimeSpan.FromSeconds(cooldownSeconds))
        {
            return false;
        }

        if (_reactionPriority > priority && now < _reactionMinimumUntil)
        {
            return false;
        }

        _cooldowns[cooldownKey] = now;
        _reactionPriority = priority;
        _reactionKey = cooldownKey;
        _reactionAcknowledgesTerminal = acknowledgesTerminal;
        _reactionMinimumUntil = now.AddSeconds(minimumSeconds);
        _reactionEndsAt = now.AddSeconds(durationSeconds);
        SetVisual(animation, null, hint, loopOverride: animation is AnimationId.Jumping or AnimationId.Waving ? false : null);
        return true;
    }

    private void ClearReaction()
    {
        _reactionPriority = 0;
        _reactionKey = null;
        _reactionAcknowledgesTerminal = false;
    }

    private bool AcknowledgeCurrentTerminals()
    {
        bool changed = false;
        foreach (ObservedTask task in _taskObservations.Values)
            if (IsTerminal(task.Snapshot.State))
                changed |= _acknowledgedTasks.Add(task.Fingerprint);
        foreach (ObservedTask task in _terminalResults.Values)
            changed |= _acknowledgedTasks.Add(task.Fingerprint);
        return changed;
    }

    private AggregateTaskState ReadTaskState(DateTimeOffset now)
    {
        _sourceArbiter.Read(now);
        var effective = new TaskStateArbiter();
        foreach (var pair in _taskObservations.ToArray())
        {
            ObservedTask task = pair.Value;
            if (task.Snapshot.ExpiresAt <= now)
            {
                _taskObservations.Remove(pair.Key);
                continue;
            }
            if (IsTerminal(task.Snapshot.State)) continue;
            effective.Observe(task.Snapshot, now);
        }
        foreach (ObservedTask task in _terminalResults.Values.Where(IsPendingTerminal))
        {
            // A source lease expires ongoing work, not a notification already observed.
            // Keep the original timestamp/fingerprint; this temporary presentation lease
            // only lets the arbiter show a still-unconfirmed terminal until P1 confirms it.
            AgentTaskSnapshot displayed = task.Snapshot.ExpiresAt <= now
                ? task.Snapshot with { ExpiresAt = now.AddSeconds(1) }
                : task.Snapshot;
            // One chat may have live work and an older pending turn simultaneously.
            // The presentation-only identity prevents its terminal from replacing that live chat.
            displayed = displayed with { Source = "pending:" + displayed.Source,
                TaskId = displayed.TaskId + "\u001f" + task.Fingerprint.Version };
            effective.Observe(displayed, now);
        }
        string[] sources = _conversationBatch.Keys.Select(key => key.Source)
            .Concat(_terminalResults.Keys.Select(key => key.Source))
            .Distinct(StringComparer.Ordinal).ToArray();
        var progressBySource = new Dictionary<string, ConversationProgress>(StringComparer.Ordinal);
        foreach (string source in sources)
        {
            ConversationProgress progress = ReadConversationProgress(now, source);
            if (progress.TotalCount > 0) progressBySource.Add(source, progress);
        }
        return effective.Read(now) with
        {
            ProgressBySource = progressBySource,
            Progress = progressBySource.GetValueOrDefault(LocalCodexTaskSource.SourceId, EmptyBatch),
            WorkBuddyProgress = progressBySource.GetValueOrDefault(LocalWorkBuddyTaskSource.SourceId, EmptyBatch)
        };
    }

    private ConversationProgress ReadConversationProgress(DateTimeOffset now, string source)
    {
        // Explicit terminal evidence remains a count while other chats in this batch continue.
        // Losing a running lease is unknown, never evidence of completion.
        var conversations = _conversationBatch.Values.Where(task => task.Source == source);
        var terminals = _terminalResults.Values.Where(task => task.Snapshot.Source == source);
        ConversationProgress progress = ConversationProgress.FromStates(
            conversations.Select(task =>
                task.ExpiresAt <= now && !IsTerminal(task.State) && task.State != AgentTaskState.Idle
                    ? AgentTaskState.Unknown : task.State)
            .Concat(terminals.Select(task => task.Snapshot.State)), currentBatch: true);
        if (progress.TotalCount == 0) return progress;
        bool pendingTerminal = terminals.Any(IsPendingTerminal);
        if (progress.ProcessingCount == 0 && progress.WaitingCount == 0 && progress.UnknownCount == 0 && !pendingTerminal)
        {
            // This also closes the late-delivery race: a quiet old completion cannot use
            // the count summary to bring back a notification already acknowledged by P1.
            foreach (var key in _conversationBatch.Keys.Where(key => key.Source == source).ToArray())
                _conversationBatch.Remove(key);
            foreach (var key in _terminalResults.Keys.Where(key => key.Source == source).ToArray())
                _terminalResults.Remove(key);
            return EmptyBatch;
        }
        return progress;
    }

    private bool TryShowTaskState(AggregateTaskState tasks)
    {
        switch (tasks.State)
        {
            case AgentTaskState.Waiting:
                SetVisual(AnimationId.Waiting, null, "等你处理任务", loopOverride: true);
                return true;
            case AgentTaskState.Failed:
                SetVisual(AnimationId.Failed, null, "任务遇到问题，点击角色确认", loopOverride: false, endFrameOverride: 4);
                return true;
            case AgentTaskState.Running:
                SetVisual(AnimationId.Running, null, "正在思考 / 工作", loopOverride: true);
                return true;
            case AgentTaskState.Review:
                SetVisual(AnimationId.Review, null, "正在审阅", loopOverride: true);
                return true;
            case AgentTaskState.Completed:
                SetVisual(AnimationId.Jumping, null, "本轮已结束，点击角色确认", loopOverride: true);
                return true;
            default:
                return false;
        }
    }

    private static bool IsTerminal(AgentTaskState state) => state is AgentTaskState.Completed or AgentTaskState.Failed;
    private bool IsPendingTerminal(ObservedTask task) => IsTerminal(task.Snapshot.State) &&
        !_acknowledgedTasks.Contains(task.Fingerprint);
    private static bool IsActiveTaskState(AgentTaskState state) => state is AgentTaskState.Running or AgentTaskState.Waiting or AgentTaskState.Review;
    private static bool HasTaskVisual(AgentTaskState state) => state is AgentTaskState.Running or AgentTaskState.Waiting or
        AgentTaskState.Review or AgentTaskState.Completed or AgentTaskState.Failed;
    private static bool IsDirectReaction(string? key) => key is "direct-wave" or "direct-jump";

    private sealed record TaskHistory(AgentTaskState State, string Version, long Generation);
    private sealed record ObservedTask(AgentTaskSnapshot Snapshot, TaskFingerprint Fingerprint);
    private readonly record struct TaskFingerprint(string Source, string TaskId, AgentTaskState State, string Version);

    private void SetVisual(AnimationId animation, int? lookIndex, string hint, bool? loopOverride = null,
        int? startFrameOverride = null, int? endFrameOverride = null)
    {
        if (_currentAnimation == animation && _currentLookIndex == lookIndex &&
            _currentLoopOverride == loopOverride && _currentStartFrameOverride == startFrameOverride &&
            _currentEndFrameOverride == endFrameOverride && string.Equals(_currentHint, hint, StringComparison.Ordinal))
        {
            return;
        }

        _currentAnimation = animation;
        _currentLookIndex = lookIndex;
        _currentHint = hint;
        _currentLoopOverride = loopOverride;
        _currentStartFrameOverride = startFrameOverride;
        _currentEndFrameOverride = endFrameOverride;
        VisualStateChanged?.Invoke(this, new VisualStateChangedEventArgs
        {
            Animation = animation,
            LookIndex = lookIndex,
            Hint = hint,
            LoopOverride = loopOverride,
            StartFrameOverride = startFrameOverride,
            EndFrameOverride = endFrameOverride
        });
    }

    public static string CategorizeWindow(string processName, string title)
    {
        string process = processName.ToLowerInvariant();
        string text = (processName + " " + title).ToLowerInvariant();

        if (ContainsAny(text, "wechat", "weixin", "微信", "qq", "discord", "slack", "teams", "telegram", "钉钉", "dingtalk"))
        {
            return "communication";
        }

        if (ContainsAny(text, "youtube", "bilibili", "哔哩", "spotify", "music", "音乐", "netflix", "video", "视频", "vlc", "potplayer"))
        {
            return "media";
        }

        if (ContainsAny(process, "codex", "code", "devenv", "rider", "idea", "pycharm", "webstorm", "terminal", "powershell", "cmd", "wt", "unity", "unreal"))
        {
            return "development";
        }

        if (ContainsAny(process, "winword", "excel", "powerpnt", "onenote", "notion", "acrobat", "sumatrapdf"))
        {
            return "document";
        }

        if (ContainsAny(process, "chrome", "msedge", "firefox", "brave", "opera", "vivaldi"))
        {
            return "browser";
        }

        return "other";
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
