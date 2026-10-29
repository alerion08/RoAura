using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoAura.Models;

namespace RoAura;

public partial class DatabaseDetailWindow
{
    private static string Value(IReadOnlyDictionary<string, string> data, string key, string fallback = "—") =>
        data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) && value is not ("null" or "None" or "[]") ? value : fallback;
    private static string Number(string value) => long.TryParse(value, out var n) ? n.ToString("N0") : value;
    private static TextBlock Body(string text) => new() { Text = text, FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 18, Margin = new Thickness(0, 4, 0, 6) };
    private Grid StatGrid(IEnumerable<(string Label, string Value)> fields)
    {
        var pairs = fields.ToList();
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        for (var c = 0; c < 4; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c % 2 == 0 ? 1 : 1.2, GridUnitType.Star) });
        for (var i = 0; i < pairs.Count; i++)
        {
            if (i % 2 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var background = new Border { Background = new SolidColorBrush(i % 4 == 0 ? Color.FromRgb(248,251,255) : Color.FromRgb(234,242,252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(214,225,238)), BorderThickness = new Thickness(0,0,0,1) };
                Grid.SetRow(background, i / 2); Grid.SetColumnSpan(background, 4); grid.Children.Add(background);
            }
            var label = new TextBlock { Text = pairs[i].Label, FontWeight = FontWeights.SemiBold, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(83,106,136)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(7,8,5,8), VerticalAlignment = VerticalAlignment.Center };
            var value = new TextBlock { Text = pairs[i].Value, FontWeight = FontWeights.Normal, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5,8,7,8), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, i / 2); Grid.SetColumn(label, i % 2 * 2);
            Grid.SetRow(value, i / 2); Grid.SetColumn(value, i % 2 * 2 + 1);
            grid.Children.Add(label); grid.Children.Add(value);
        }
        return grid;
    }
    private void CollapsedSection(string title, UIElement content) => DetailsPanel.Children.Add(new Expander
    {
        Header = title, IsExpanded = false, Margin = new Thickness(0,8,0,2), FontWeight = FontWeights.SemiBold,
        Content = new Border { Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(248,251,255)), Child = content }
    });

    private void ShowMonsterStatistics(DatabaseMonster m)
    {
        AddSection(T("Характеристики", "Stats"));
        if (_database.Version != GameDatabaseVersion.Toc)
        {
            DetailsPanel.Children.Add(StatGrid(m.Stats.Select(p => (MonsterField(p.Key), p.Value))));
        }
        else
        {
            var stats = m.Stats;
            DetailsPanel.Children.Add(StatGrid(new (string,string)[] {
                (T("Уровень", "Level"), m.Level), ("HP", Number(m.Hp)),
                ("Base EXP", Number(m.BaseExp)), ("Job EXP", Number(m.JobExp)),
                (T("Атака", "Attack"), m.Attack), ("DEF / MDEF", m.Defense + " / " + m.MagicDefense),
                (T("Раса", "Race"), m.Race), (T("Элемент", "Element"), m.Element),
                (T("Размер", "Size"), m.Size), (T("Класс", "Class"), Value(stats, "class")),
                ("STR", Value(stats,"str")), ("AGI", Value(stats,"agi")), ("VIT", Value(stats,"vit")), ("INT", Value(stats,"int")),
                ("DEX", Value(stats,"dex")), ("LUK", Value(stats,"luk")), ("HIT", Value(stats,"hit")), ("FLEE", Value(stats,"flee"))
            }));
            var extra = new StackPanel();
            extra.Children.Add(StatGrid(new (string,string)[] {
                ("MVP EXP", Number(Value(stats,"MVP EXP", "0"))),
                (T("Дальность атаки", "Attack range"), Value(stats,"attack_range")),
                (T("Дальность навыков", "Skill range"), Value(stats,"skill_range")),
                (T("Дальность погони", "Chase range"), Value(stats,"chase_range")),
                (T("Интервал шага, мс", "Walk interval, ms"), Value(stats,"walk_speed")),
                (T("Задержка атаки, мс", "Attack delay, ms"), Value(stats,"attack_delay"))
            }));
            extra.Children.Add(Body(T("Поведение: ", "Behavior: ") + ArrayText(Value(stats,"ai_modes"))));
            CollapsedSection(T("Дополнительные характеристики", "More stats"), extra);
        }
        if (m.ElementDamage.Count > 0) CollapsedSection(T("Урон стихий", "Element damage"), StatGrid(m.ElementDamage.Select(p => (MonsterField(p.Key),p.Value))));
        if (m.Behavior.Count > 0) CollapsedSection(T("Поведение", "Behavior"), StatGrid(m.Behavior.Select(p => (MonsterField(p.Key),MonsterValue(p.Value)))));
    }
    private void ShowMonsterSkills(IEnumerable<DatabaseMonsterSkill> skills)
    {
        var list = skills.ToList();
        if (list.Count == 0) return;
        AddSection(T($"Навыки ({list.Count})", $"Skills ({list.Count})"));
        foreach (var skill in list)
        {
            var body = new StackPanel();
            if (skill.Description.Length > 0) body.Children.Add(Body(skill.Description));
            try
            {
                using var rules = JsonDocument.Parse(skill.Type);
                if (rules.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var rule in rules.RootElement.EnumerateArray())
                    {
                        string Get(string key) => rule.TryGetProperty(key,out var v) ? v.ToString() : "—";
                        body.Children.Add(StatGrid(new (string,string)[] {
                            (T("Состояние", "State"), Get("state")), (T("Цель", "Target"), Get("target")),
                            (T("Условие", "Condition"), Get("condition")), (T("Шанс, %", "Chance, %"), Get("chance")),
                            (T("Каст", "Cast"), Get("cast")), (T("Задержка", "Delay"), Get("delay")) }));
                    }
            }
            catch (JsonException) { if (skill.Type.Length > 0) body.Children.Add(Body(skill.Type)); }
            CollapsedSection(skill.Name + (skill.Level.Length > 0 ? " · Lv " + skill.Level : ""), body);
        }
    }
    private void ShowTocItemInformation(DatabaseItem item)
    {
        var data = item.Stats;
        var fields = new List<(string,string)> {
            (T("Тип", "Type"), Value(data,"type_label", item.Type)), (T("Подтип", "Subtype"), Value(data,"subtype_label")),
            (T("Вес", "Weight"), Value(data,"Weight")), (T("Слоты", "Slots"), Value(data,"slots", "0")),
            (T("Покупка NPC", "NPC buy"), Number(Value(data,"price_buy", "0")) + " Z"),
            (T("Продажа NPC", "NPC sell"), item.NpcSellPrice.ToString("N0") + " Z")
        };
        if (item.Type is "Weapon" or "Armor")
        {
            fields.Add((T("Атака", "Attack"), Value(data,"attack","0"))); fields.Add((T("Защита", "Defense"),Value(data,"defense","0")));
            fields.Add((T("Нужный уровень", "Required level"),Value(data,"equip_level_min","0"))); fields.Add((T("Уровень оружия", "Weapon level"),Value(data,"weapon_level","0")));
        }
        DetailsPanel.Children.Add(StatGrid(fields));
        var jobs = ArrayText(Value(data,"job_names"));
        if (jobs != "—") DetailsPanel.Children.Add(Body(T("Профессии: ", "Jobs: ") + jobs));
        if (item.Description.Length > 0) { AddSection(T("Описание и эффекты", "Description & effects")); DetailsPanel.Children.Add(Body(item.Description)); }
        var scripts = new StackPanel();
        foreach (var key in new[] { "script", "equip_script", "unequip_script" })
            if (Value(data,key, "").Length > 0) scripts.Children.Add(new TextBlock { Text = key + ":\n" + data[key], FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,8) });
        if (scripts.Children.Count > 0) CollapsedSection(T("Скрипты предмета", "Item scripts"), scripts);
        var restrictions = new StackPanel();
        if (item.Type is "Weapon" or "Armor") restrictions.Children.Add(Body(T("Заточка: ", "Refineable: ") + (Value(data,"refineable") == "True" ? T("да","yes") : T("нет","no"))));
        foreach (var key in new[] { "jobs", "classes", "locations", "costume_locations", "shadow_locations", "trade", "stack" })
        {
            var text = RestrictionText(Value(data,key,""));
            if (text.Length > 0) restrictions.Children.Add(Body(key + ": " + text));
        }
        if (restrictions.Children.Count > 0) CollapsedSection(T("Условия и ограничения", "Requirements & restrictions"), restrictions);
    }
    private static string RestrictionText(string text)
    {
        if (text.Length == 0) return "";
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => string.Join(", ", doc.RootElement.EnumerateArray().Select(x => x.ToString())),
                JsonValueKind.Object => string.Join(", ", doc.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.Number && p.Value.ToString() != "0").Select(p => p.Name + " = " + p.Value)),
                _ => ""
            };
        }
        catch (JsonException) { return text; }
    }
    private static string ArrayText(string text) { var value = RestrictionText(text); return value.Length == 0 ? "—" : value; }
}
