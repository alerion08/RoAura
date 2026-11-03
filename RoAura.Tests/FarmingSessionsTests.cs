using System.Text.Json;
using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class FarmingSessionsTests
{
    private static readonly DateTime Start = new(2026,11,3,12,0,0,DateTimeKind.Utc);
    [Fact]
    public void Pause_excludes_time_income_and_loot_and_updates_the_balance_baseline()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoAura-farming-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CharacterStatisticsStore(directory);
            var ledger = new CharacterStatisticsLedger(store);
            var key = CharacterStatisticsKey.Create("SpringRO",123);
            ledger.SwitchTo(key,Start);
            ledger.AddExperience(100,false,Start);
            ledger.AddExperience(30,true,Start);
            ledger.ObserveZeny(1000,Start);
            ledger.SetPaused(true,Start.AddMinutes(2));
            Assert.False(ledger.AddExperience(900,false,Start.AddMinutes(3)));
            Assert.False(ledger.AddLoot(501,"Potion",4,Start.AddMinutes(3)));
            Assert.Equal(0,ledger.ObserveZeny(1500,Start.AddMinutes(3)));
            Assert.Equal(TimeSpan.FromMinutes(2),ledger.ActiveSessionElapsed(Start.AddHours(1)));
            ledger.Flush(Start.AddHours(1));
            var restored = new CharacterStatisticsLedger(store);
            restored.SwitchTo(key,Start.AddHours(2));
            Assert.True(restored.Snapshot(key,Start.AddHours(2))!.SessionPaused);
            Assert.Equal(TimeSpan.FromMinutes(2),restored.ActiveSessionElapsed(Start.AddHours(3)));
            ledger.SetPaused(false,Start.AddMinutes(10));
            Assert.Equal(100,ledger.ObserveZeny(1600,Start.AddMinutes(11)));
            ledger.AddExperience(50,true,Start.AddMinutes(11));
            var state=ledger.Snapshot(key,Start.AddMinutes(12))!;
            Assert.Equal(100,state.SessionBaseExperience);
            Assert.Equal(80,state.SessionJobExperience);
            Assert.Equal(100,state.SessionZenyGained);
            Assert.Equal(TimeSpan.FromMinutes(4).Ticks,state.SessionActiveTicks);
            Assert.Empty(state.SessionLoot);
            var id=state.SessionId;
            ledger.StartNewSession(Start.AddMinutes(12));
            Assert.NotEqual(id,ledger.Snapshot(key,Start.AddMinutes(12))!.SessionId);
            Assert.False(ledger.Snapshot(key,Start.AddMinutes(12))!.SessionPaused);
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }

    [Fact]
    public void History_freezes_each_pickup_price_survives_restart_and_deletion_cancels_writes()
    {
        var directory=Path.Combine(Path.GetTempPath(),"RoAura-history-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new FarmingSessionHistoryStore(directory);
            var session=new FarmingSession {Id=Guid.NewGuid(),Server="SPRINGRO",CharacterId=123,
                CharacterName="Knight",StartedUtc=Start,UpdatedUtc=Start,ZenyGained=250,
                Loot=[new() {ReceivedUtc=Start,ItemId=501,Name="Potion",Count=2,UnitPrice=50,PriceSource="Vending",PriceKnown=true}]};
            store.Queue(session);
            session.Loot[0].UnitPrice=999;
            store.Flush();
            var restored=new FarmingSessionHistoryStore(directory);
            Assert.Equal(100,Assert.Single(restored.List()).LootValue);
            session.Loot[0].UnitPrice=50;
            session.Loot.Add(new() {ReceivedUtc=Start.AddMinutes(1),ItemId=501,Name="Potion",Count=1,UnitPrice=75,PriceSource="Vending",PriceKnown=true});
            store.Queue(session); store.Flush();
            var captured=store.Read(session.Id)!;
            Assert.Equal(175,captured.LootValue);
            Assert.Equal(new long[] {50,75},captured.Loot.Select(row=>row.UnitPrice));
            store.Delete(session.Id);
            store.Flush();
            Assert.False(File.Exists(Path.Combine(directory,session.Id.ToString("N")+".json")));
            restored=new FarmingSessionHistoryStore(directory);
            restored.Queue(session);restored.Flush();
            Assert.Empty(restored.List());
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }

    [Fact]
    public void Loot_and_experience_extend_the_same_quiet_window_but_separate_bursts_do_not_merge()
    {
        var group=new RewardGroupSettings {AggregationWindowMs=650,DurationSeconds=3};
        var xp=new RewardNotification {Kind=RewardKind.Experience,LastUpdatedUtc=Start,ExpiresAtUtc=Start.AddSeconds(3)};
        group.Notifications.Add(xp);
        RewardAggregation.Refresh(group,Start.AddMilliseconds(500));
        group.Notifications.Add(new RewardNotification {Kind=RewardKind.Loot,LastUpdatedUtc=Start.AddMilliseconds(500),ExpiresAtUtc=Start.AddSeconds(3.5)});
        RewardAggregation.Refresh(group,Start.AddMilliseconds(1000));
        Assert.Equal(Start.AddMilliseconds(1000),xp.LastUpdatedUtc);
        Assert.Equal(Start.AddSeconds(4),xp.ExpiresAtUtc);
        RewardAggregation.Refresh(group,Start.AddMilliseconds(1700));
        Assert.Equal(Start.AddMilliseconds(1000),xp.LastUpdatedUtc);
    }

    [Fact]
    public void Suppressed_market_baseline_is_persistent_and_only_new_offers_alert()
    {
        var rule=new MarketWatchRule {ItemId=501,ItemName="Potion",WithoutPrice=true,SuppressCurrentOffers=true};
        var first=new MarketListing {ItemId=501,Price="100",Owner="Alice",ShopType="SELL"};
        var second=new MarketListing {ItemId=501,Price="90",Owner="Bob",ShopType="SELL"};
        var service=new MarketWatchService();
        Assert.Empty(service.Evaluate([rule],[first]));
        rule=JsonSerializer.Deserialize<MarketWatchRule>(JsonSerializer.Serialize(rule))!;
        service=new MarketWatchService();
        Assert.Empty(service.Evaluate([rule],[first]));
        Assert.Equal(90,Assert.Single(service.Evaluate([rule],[first,second])).Price);
        Assert.Empty(service.Evaluate([rule],[first,second]));
    }

    [Fact]
    public void New_indicators_default_off_and_hotkey_conflicts_do_not_replace_existing_bindings()
    {
        var settings=new AppSettings();
        settings.SyncRateIndicators();
        Assert.False(settings.RateIndicators.Single(i=>i.Kind==RateIndicatorKind.TotalBaseExperience).Enabled);
        Assert.False(settings.RateIndicators.Single(i=>i.Kind==RateIndicatorKind.TotalJobExperience).Enabled);
        settings.PauseSessionHotkey=settings.DatabaseHotkey;
        Assert.False(FarmingHotkeys.Enabled(settings,settings.PauseSessionHotkey));
        Assert.Equal("F7",settings.DatabaseHotkey);
        Assert.True(GlobalInputHook.IsAlwaysForwarded(0x2C));
        Assert.True(GlobalInputHook.IsAlwaysForwarded(0x11));
        Assert.False(GlobalInputHook.IsAlwaysForwarded(0x41));
    }

    [Fact]
    public void Excluded_item_is_zero_even_with_a_manual_override_and_map_changes_clear_the_marker()
    {
        var row=new GoldGoblinLootRow {Count=4,Cost=500,Excluded=true};
        Assert.Equal(0,row.Total);
        row.Excluded=false;Assert.Equal(2000,row.Total);
        var timer=new MvpTimer {MonsterId=1039,LocationCode="prt_maze03",MarkerX=.3,MarkerY=.7};
        timer=JsonSerializer.Deserialize<MvpTimer>(JsonSerializer.Serialize(timer))!;
        Assert.Equal(.3,timer.MarkerX);
        timer.LocationCode="mjolnir_04";
        Assert.Null(timer.MarkerX);Assert.Null(timer.MarkerY);
    }
}
