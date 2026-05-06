using catgirlwindow.SrcModels;
using catgirlwindow.Src.Services;

namespace catgirlwindow.SrcTests;

public class AgentMoodTrackerTests
{
    private readonly AgentMoodTracker _tracker;

    public AgentMoodTrackerTests()
    {
        _tracker = new AgentMoodTracker();
    }

    [Fact]
    public void InitialState_ShouldBeNeutral()
    {
        Assert.Equal(AgentMood.Neutral, _tracker.CurrentMood);
    }

    [Fact]
    public void SetMood_ShouldChangeCurrentMood()
    {
        _tracker.SetMood(AgentMood.Happy);
        Assert.Equal(AgentMood.Happy, _tracker.CurrentMood);
    }

    [Fact]
    public void SetMood_ShouldFireMoodChangedEvent()
    {
        var fired = false;
        AgentMood capturedMood = AgentMood.Neutral;
        _tracker.MoodChanged += (_, mood) => { fired = true; capturedMood = mood; };
        _tracker.SetMood(AgentMood.Happy);
        Assert.True(fired);
        Assert.Equal(AgentMood.Happy, capturedMood);
    }

    [Fact]
    public void SetMood_SameMood_ShouldNotFireEvent()
    {
        var fired = false;
        _tracker.MoodChanged += (_, _) => fired = true;
        _tracker.SetMood(AgentMood.Neutral);
        Assert.False(fired);
    }

    [Fact]
    public void UpdateMoodByEvent_LateNight_ShouldSetSleepy()
    {
        _tracker.UpdateMoodByEvent("LateNight");
        Assert.Equal(AgentMood.Sleepy, _tracker.CurrentMood);
    }

    [Fact]
    public void UpdateMoodByEvent_Pet_ShouldSetTouched()
    {
        _tracker.UpdateMoodByEvent("Pet");
        Assert.Equal(AgentMood.Touched, _tracker.CurrentMood);
    }

    [Fact]
    public void UpdateMoodByEvent_Compliment_ShouldSetHappy()
    {
        _tracker.UpdateMoodByEvent("Compliment");
        Assert.Equal(AgentMood.Happy, _tracker.CurrentMood);
    }

    [Fact]
    public void UpdateMoodByEvent_LongWork_ShouldSetNeutral()
    {
        _tracker.UpdateMoodByEvent("LongWork");
        Assert.Equal(AgentMood.Neutral, _tracker.CurrentMood);
    }

    [Fact]
    public void UpdateMoodByEvent_Idle_ShouldSetSad()
    {
        _tracker.UpdateMoodByEvent("Idle");
        Assert.Equal(AgentMood.Sad, _tracker.CurrentMood);
    }

    [Fact]
    public void UpdateMoodByEvent_Active_ShouldSetNeutral()
    {
        _tracker.UpdateMoodByEvent("Active");
        Assert.Equal(AgentMood.Neutral, _tracker.CurrentMood);
    }

    [Fact]
    public void UpdateMoodByEvent_Unknown_ShouldNotChange()
    {
        _tracker.SetMood(AgentMood.Happy);
        _tracker.UpdateMoodByEvent("UnknownEvent");
        Assert.Equal(AgentMood.Happy, _tracker.CurrentMood);
    }

    [Fact]
    public void SetMood_MultipleTimes_ShouldFireEventEachTime()
    {
        var fireCount = 0;
        _tracker.MoodChanged += (_, _) => fireCount++;
        _tracker.SetMood(AgentMood.Happy);
        _tracker.SetMood(AgentMood.Sad);
        _tracker.SetMood(AgentMood.Angry);
        Assert.Equal(3, fireCount);
    }

    [Fact]
    public void GetMoodImagePath_ShouldReturnNonEmptyPath()
    {
        var path = _tracker.GetMoodImagePath();
        Assert.False(string.IsNullOrEmpty(path));
    }

    [Fact]
    public void GetMoodImagePath_Happy_ShouldContainHappy()
    {
        _tracker.SetMood(AgentMood.Happy);
        var path = _tracker.GetMoodImagePath();
        Assert.Contains("happy", path.ToLowerInvariant());
    }

    [Fact]
    public void GetMoodImagePath_Sad_ShouldContainSad()
    {
        _tracker.SetMood(AgentMood.Sad);
        var path = _tracker.GetMoodImagePath();
        Assert.Contains("sad", path.ToLowerInvariant());
    }

    [Fact]
    public void GetMoodImagePath_Sleepy_ShouldContainSleepy()
    {
        _tracker.SetMood(AgentMood.Sleepy);
        var path = _tracker.GetMoodImagePath();
        Assert.Contains("sleepy", path.ToLowerInvariant());
    }

    [Fact]
    public void GetMoodImagePath_Touched_ShouldContainTouched()
    {
        _tracker.SetMood(AgentMood.Touched);
        var path = _tracker.GetMoodImagePath();
        Assert.Contains("touched", path.ToLowerInvariant());
    }

    [Fact]
    public void GetMoodImagePath_Angry_ShouldContainAngry()
    {
        _tracker.SetMood(AgentMood.Angry);
        var path = _tracker.GetMoodImagePath();
        Assert.Contains("angry", path.ToLowerInvariant());
    }

    [Fact]
    public void MoodChanged_EventHandler_ShouldBeThreadSafe()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var moods = new[] { AgentMood.Happy, AgentMood.Sad, AgentMood.Angry, AgentMood.Neutral };
                var mood = moods[Random.Shared.Next(moods.Length)];
                _tracker.SetMood(mood);
            }));
        }
        Task.WaitAll(tasks.ToArray());
        Assert.Contains(_tracker.CurrentMood, new[] { AgentMood.Happy, AgentMood.Sad, AgentMood.Angry, AgentMood.Neutral });
    }

    [Fact]
    public void UpdateMoodByEvent_Pet_AfterSad_ShouldChangeTouched()
    {
        _tracker.SetMood(AgentMood.Sad);
        _tracker.UpdateMoodByEvent("Pet");
        Assert.Equal(AgentMood.Touched, _tracker.CurrentMood);
    }

    [Fact]
    public void UpdateMoodByEvent_Compliment_AfterAngry_ShouldChangeHappy()
    {
        _tracker.SetMood(AgentMood.Angry);
        _tracker.UpdateMoodByEvent("Compliment");
        Assert.Equal(AgentMood.Happy, _tracker.CurrentMood);
    }

    [Fact]
    public void SetMood_AllMoods_ShouldWork()
    {
        foreach (AgentMood mood in Enum.GetValues<AgentMood>())
        {
            _tracker.SetMood(mood);
            Assert.Equal(mood, _tracker.CurrentMood);
        }
    }

    [Fact]
    public void GetMoodImagePath_AllMoods_ShouldReturnNonEmpty()
    {
        foreach (AgentMood mood in Enum.GetValues<AgentMood>())
        {
            _tracker.SetMood(mood);
            var path = _tracker.GetMoodImagePath();
            Assert.False(string.IsNullOrEmpty(path), $"Path should not be empty for mood {mood}");
        }
    }
}
