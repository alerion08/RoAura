using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using RoAura.Controls;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

[Collection("WPF UI")]
public sealed class MvpMapLoadingTests
{
    private static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGOU0zD6z8DAwMDEAAUAFZQBe2cQC1QAAAAASUVORK5CYII=");
    [Fact]
    public async Task Concurrent_requests_share_a_fetch_and_persist_complete_frozen_images()
    {
        var directory=Path.Combine(Path.GetTempPath(),"RoAura-MapTest-"+Guid.NewGuid());
        try
        {
            var calls=0;
            var download=new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cache=new MvpMapImageCache(_=>{Interlocked.Increment(ref calls);return download.Task;},directory,directory);
            var first=cache.GetAsync("First");var second=cache.GetAsync(" first ");
            download.SetResult(Png);
            var images=await Task.WhenAll(first,second);
            Assert.Equal(1,calls);Assert.NotNull(images[0]);Assert.Same(images[0],images[1]);Assert.True(images[0]!.IsFrozen);
            var offline=new MvpMapImageCache(_=>throw new InvalidOperationException("Network must not be used"),directory,directory);
            Assert.NotNull(await offline.GetAsync("first"));
            Assert.Null(await offline.GetAsync("../first"));
        }
        finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Fact]
    public async Task Failed_download_can_be_retried_without_changing_the_map()
    {
        var directory=Path.Combine(Path.GetTempPath(),"RoAura-MapRetry-"+Guid.NewGuid());
        try
        {
            var available=false;
            var cache=new MvpMapImageCache(_=>Task.FromResult<byte[]?>(available?Png:null),directory,directory);
            Assert.Null(await cache.GetAsync("first"));
            available=true;
            Assert.NotNull(await cache.GetAsync("first"));
        }
        finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Fact]
    public void Delayed_previous_map_never_overwrites_the_new_choice_and_reopened_editor_paints()
    {
        RunSta(async ()=>
        {
            var directory=Path.Combine(Path.GetTempPath(),"RoAura-MapSwitch-"+Guid.NewGuid());
            var first=new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second=new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cache=new MvpMapImageCache(code=>code=="first"?first.Task:second.Task,directory,directory);
            var timer=new MvpTimer();
            timer.SetMonsterDetails(new MonsterEntry {Spawns=[new(){Code="first"},new(){Code="second"}]});
            var editor=new MvpMapEditor(cache) {DataContext=timer};
            var window=new Window {Content=editor,Width=300,Height=300};
            try
            {
                window.Show();window.UpdateLayout();
                Assert.Equal("first",timer.LocationCode);
                timer.LocationCode="second";
                second.SetResult(Png);
                await Wait(()=>editor.IsMapReady);
                Assert.Equal("second",editor.DisplayedMapCode);
                first.SetResult(Png);await cache.GetAsync("first");await Task.Delay(50);
                Assert.Equal("second",editor.DisplayedMapCode);
                var reopened=new MvpMapEditor(cache) {DataContext=timer};
                window.Content=reopened;window.UpdateLayout();
                await Wait(()=>reopened.IsMapReady);
                Assert.Equal("second",reopened.DisplayedMapCode);
            }
            finally
            {
                first.TrySetResult(Png);second.TrySetResult(Png);
                window.Close();await Task.WhenAll(cache.GetAsync("first"),cache.GetAsync("second"));
                if(Directory.Exists(directory))Directory.Delete(directory,true);
            }
        });
    }
    [Fact]
    public void Restart_replaces_the_old_kill_clock_and_end_time()
    {
        var timer=new MvpTimer {DurationMinutes=60};
        timer.StartFromKillTime(DateTime.Now.AddMinutes(-25));timer.CompletionSoundPlayed=true;
        var old=timer.EndsAtUtc;var before=DateTime.UtcNow;
        timer.Restart();
        Assert.True(timer.EndsAtUtc>old);Assert.True(timer.KilledAtUtc>=before);
        Assert.InRange(timer.RemainingSeconds,3599,3600);Assert.False(timer.CompletionSoundPlayed);
    }
    private static async Task Wait(Func<bool> condition)
    {
        var end=DateTime.UtcNow.AddSeconds(5);
        while(!condition()&&DateTime.UtcNow<end)await Task.Delay(10);
        Assert.True(condition(),"Map did not paint.");
    }
    private static void RunSta(Func<Task> action)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async ()=>
            {
                try {await action();}
                catch(Exception ex){failure=ex;}
                finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)),"Map UI test timed out.");
        if(failure is not null)ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
