using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class DatabaseOverlayWindow
{
    internal void SaveDatabaseSmokeSnapshots(string directory)
    {
        Directory.CreateDirectory(directory);
        var reference = new GameDatabaseService(GameDatabaseVersion.Toc);
        var monster = reference.Monsters.First(item => item.Drops.Count > 0 && item.Spawns.Count > 0);
        var item = reference.Item(999) ?? reference.Items.First();
        var map = reference.Map("alde_dun01") ?? reference.Maps.First();
        var barter = RagnarokPlusBarterCatalog.Search("").FirstOrDefault()?.Value;
        var market = new MarketListing { ItemId = item.Id, ItemName = item.Name, Price = "12345678", Quantity = "20",
            Stall = "Fixture stall", Owner = "Fixture seller", GameMode = "SC", ShopType = "SELL", Map = "", };
        var barterVisibility = BarterMode.Visibility;
        var marketEnabled = MarketMode.IsEnabled;
        try
        {
            BarterMode.Visibility = Visibility.Visible;
            MarketMode.IsEnabled = true;
            var cases = new (TabItem Tab, string Name, DatabaseSearchKind Kind, object? Value, string Caption)[]
            {
                (MonstersMode, "monsters", DatabaseSearchKind.Monsters, monster, monster.Name),
                (ItemsMode, "items", DatabaseSearchKind.Items, item, item.Name),
                (MapsMode, "maps", DatabaseSearchKind.Maps, map, map.Name),
                (BarterMode, "barter", DatabaseSearchKind.Barter, barter, "Barter"),
                (MarketMode, "market", DatabaseSearchKind.Market, market, market.ItemName)
            };
            foreach (var dark in new[] { false, true })
            {
                var mode = dark ? "dark" : "light";
                DatabasePresentation.ApplyPalette(this, dark);
                DatabasePresentation.ApplyPalette(_details, dark);
                AppearanceService.ApplyPalette(this, dark ? "Dark" : "Light");
                AppearanceService.ApplyPalette(_details, dark ? "Dark" : "Light");
                foreach (var (tab, name, kind, value, caption) in cases)
                {
                    if (value is null) continue;
                    tab.IsSelected = true;
                    ResultsList.ItemsSource = new[] { new DatabaseSearchResult(kind, value, caption, "") };
                    Snapshot((FrameworkElement)Content, $"database-{name}-{mode}-search", dark ? 320 : 382);
                    _details.ResetDetails();
                    _details.ShowDetails(value);
                    Snapshot((FrameworkElement)_details.Content, $"database-{name}-{mode}-card", dark ? 340 : 406);
                }
            }
        }
        finally
        {
            BarterMode.Visibility = barterVisibility;
            MarketMode.IsEnabled = marketEnabled;
            MonstersMode.IsSelected = true;
            _details.ResetDetails();
            UpdateDatabasePalette();
            AppearanceService.ApplyWindow(this);
            AppearanceService.ApplyWindow(_details);
            _details.RefreshSmokePalette();
            RefreshResults();
        }

        void Snapshot(FrameworkElement surface, string name, int width)
        {
            const int height = 620;
            surface.Measure(new Size(width, height));
            surface.Arrange(new Rect(0, 0, width, height));
            surface.UpdateLayout();
            var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
        }
    }
}
