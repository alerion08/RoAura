using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RoAura.Models;
using RoAura.Services;
namespace RoAura;

public partial class QuestDetailsControl : UserControl
{
 public event Action<QuestGuide?>? GuideRequested;
 private readonly QuestSemanticService _reader=new();
 private readonly DispatcherTimer _refresh=new(){Interval=TimeSpan.FromSeconds(2)};
 private readonly DispatcherTimer _toast=new(){Interval=TimeSpan.FromSeconds(3)};
 private SemanticCatalog? _catalog;private SemanticQuest? _quest;private QuestWorkspace? _workspace;
 private string? _selectedId,_semanticChoice,_workspaceKey;private string _language="ru",_fingerprint="";private bool _loading,_rendering;private Task? _loadTask;private FileSystemWatcher? _watcher;
 private readonly Dictionary<string,Border> _cards=new();
 private bool Editable=>_workspace?.CanEdit==true && _quest is not null && SemanticReadiness.MatchesProgress(_quest,_workspace.SelectedGuide);
 public QuestDetailsControl()
 {
  InitializeComponent();_refresh.Tick+=async(_,_)=>await RefreshDataAsync();
  Loaded+=async(_,_)=>{WatchDatabase();_refresh.Start();await RefreshDataAsync(true);};Unloaded+=(_,_)=>{_refresh.Stop();_watcher?.Dispose();_watcher=null;};
  _toast.Tick+=(_,_)=>{_toast.Stop();Toast.Visibility=Visibility.Collapsed;};
 }
 private void WatchDatabase()
 {
  _watcher?.Dispose();var directory=Path.GetDirectoryName(_reader.Path);
  if(directory is null || !Directory.Exists(directory))return;
  _watcher=new FileSystemWatcher(directory,Path.GetFileName(_reader.Path)){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.FileName|NotifyFilters.CreationTime};
  void Invalidate(){if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(()=>_fingerprint=""));}
  _watcher.Changed+=(_,_)=>Invalidate();_watcher.Created+=(_,_)=>Invalidate();_watcher.Deleted+=(_,_)=>Invalidate();_watcher.Renamed+=(_,_)=>Invalidate();_watcher.Error+=(_,_)=>Invalidate();_watcher.EnableRaisingEvents=true;
 }
 private string L(string ru,string en)=>_language=="ru"?ru:en;
 private static Brush B(string color)=>(Brush)new BrushConverter().ConvertFromString(color)!;
 private static TextBlock Text(string value,double size=12,bool bold=false)=>new(){Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,4)};
 public Task RefreshDataAsync(bool force=false)
 {
  if(_loadTask is {IsCompleted:false})return _loadTask;
  var file=new FileInfo(_reader.Path);var fingerprint=file.Exists?$"{file.Length}:{file.LastWriteTimeUtc.Ticks}":"missing";
  if(!force && fingerprint==_fingerprint)return Task.CompletedTask;
  return _loadTask=LoadDataAsync(fingerprint);
 }
 private async Task LoadDataAsync(string fingerprint)
 {
  _loading=true;
  try{_catalog=await _reader.LoadAsync();_fingerprint=fingerprint;DataStatus.Text="";}
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException)
  {_catalog=null;_fingerprint=fingerprint;DataStatus.Text=L("Семантическая база недоступна: ","Semantic database unavailable: ")+ex.Message;}
  finally{_loading=false;if(_workspace is not null)Render(_workspace);}
 }
 public void Render(QuestWorkspace workspace)
 {
  _workspace=workspace;
  if(_catalog is null && !_loading && _fingerprint=="")_ = RefreshDataAsync();
  _rendering=true;
  try
  {
   _language=LocalizationService.CurrentLanguage=="ru"?"ru":"en";
   var previous=_quest?.Id;var workspaceKey=workspace.SelectedGuide?.Key;
   if(workspaceKey!=_workspaceKey){_semanticChoice=null;_workspaceKey=workspaceKey;}
   var key=_semanticChoice??workspaceKey;_quest=key is null?null:_catalog?.Quests.GetValueOrDefault(key);
   ReadyChoice.ItemsSource=_catalog?.Quests.Values.OrderBy(q=>q.Title).ToList();ReadyChoice.SelectedValue=_quest?.Id;
   if(_quest?.Id!=previous)_selectedId=null;
   if(_quest is not null && !_quest.Steps.Any(s=>s.Id==_selectedId))_selectedId=Current()?.Id??_quest.Steps[0].Id;
   RenderChrome();RenderSteps();RenderDetail();RenderFacts();RenderSources();
   if(previous!=_quest?.Id && Current()?.Id==_selectedId)
       Dispatcher.BeginInvoke(ScrollToCurrent,DispatcherPriority.ContextIdle);
  }
  finally{_rendering=false;}
 }
 private SemanticStep? Current()=>_workspace?.SelectedGuide?.Key==_quest?.Id?_quest?.Steps.FirstOrDefault(s=>_workspace?.IsCurrentStep(s.Order)==true):null;
 private QuestStep? Legacy(SemanticStep step)=>Editable?_workspace?.SelectedGuide?.Steps.SingleOrDefault(s=>s.Number==step.Order):null;
 private bool Done(SemanticStep step)=>Legacy(step) is {} legacy && _workspace!.IsStepDone(_workspace.SelectedGuide!,legacy);
 private void RenderChrome()
 {
  QuestTitle.Text=_quest?.Title??L("Выберите полностью локализованный квест","Select a fully localized quest");
  Metadata.Text=_quest is null?"":$"{L("Ревизия источника","Source revision")}: {_quest.Source.Revision[..Math.Min(10,_quest.Source.Revision.Length)]} · Base Lv. {_quest.BaseLevel?.ToString()??L("не указан","not specified")} · Job Lv. {_quest.JobLevel?.ToString()??L("не указан","not specified")}";
  Metadata.ToolTip=_quest?.Source.Revision;
  Quality.Text=_quest is null?L("Новый экран доступен только для квестов, все этапы которых готовы RU/EN.","The new screen accepts only quests with every step ready in RU/EN."):
   L("Описания: готовы RU/EN · Прохождение на сервере: статус неизвестен","Descriptions: RU/EN ready · Server walkthrough: status unknown");
  if(_quest is not null && !Editable)Quality.Text+="\n"+L("Изменение прогресса недоступно: выберите профиль; при несовпадении этапов требуется сверка каталога.","Progress editing unavailable: select a profile; mismatching steps require catalog review.");
  var active=_quest is not null && _workspace?.SelectedGuide?.Key==_quest.Id && _workspace.IsActive(_workspace.SelectedGuide);
  AddButton.Content=active?L("✓ Активен","✓ Active"):L("+ Добавить в активные","+ Add to active");AddButton.IsEnabled=Editable;
  if(active)QuestCopyFeedback.ColorState(AddButton,B("#298E38"));else{AddButton.ClearValue(Button.TemplateProperty);AddButton.ClearValue(Button.ForegroundProperty);}
  GoCurrentButton.Content=L("К текущему этапу","Go to current step");GoCurrentButton.IsEnabled=Current() is not null;
  WalkTab.Header=L("Прохождение","Walkthrough");FactsTab.Header=L("Требования и награды","Requirements and rewards");SourcesTab.Header=L("Источники","Sources");
  var done=_quest?.Steps.Count(Done)??0;var count=_quest?.Steps.Count??0;
  ProgressText.Text=L("Выполнено: ","Completed: ")+$"{done}/{count}";ProgressBar.Value=count==0?0:(double)done/count;
  foreach(var (button,language) in new[]{(RuButton,"ru"),(EnButton,"en")})
  {button.Background=B(language==_language?"#1F67C2":"#F3F7FD");button.Foreground=language==_language?Brushes.White:B("#1C4782");}
 }
 private void RenderSteps()
 {
  var offset=WalkScroll.VerticalOffset;StepsPanel.Children.Clear();_cards.Clear();
  if(_quest is null)return;
  foreach(var step in _quest.Steps)
  {
   var done=Done(step);var selected=step.Id==_selectedId;
   var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(22)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(19)});
   var number=Text(step.Order+".",12,true);number.Foreground=B(done?"#5E6D80":"#0D3268");grid.Children.Add(number);
   var content=new StackPanel {Margin=new Thickness(7,0,7,0),Opacity=done ? 0.62 : 1};Grid.SetColumn(content,1);grid.Children.Add(content);
   var excerpt=Text("");ApplyText(excerpt,step,false,done);content.Children.Add(excerpt);
   if(SemanticCoordinates.All(step).Count>0)content.Children.Add(Coordinates(step,done));
   var actions=new WrapPanel{Margin=new Thickness(0,5,0,0)};
   var use=new Button {Content=L("Выбрать этап","Select step"),FontSize=11,Height=24,MinHeight=24,Padding=new Thickness(10,0,10,0),IsEnabled=Editable && _workspace!.HasProgress(_workspace.SelectedGuide)};
   use.Click+=(_,e)=>{e.Handled=true;_selectedId=step.Id;_workspace!.SelectStep(step.Order);DetailsTabs.SelectedIndex=0;};actions.Children.Add(use);
   if(Current()?.Id==step.Id)actions.Children.Add(Tag(L("Текущий этап","Current step"),done?"#687787":"#1764CC"));content.Children.Add(actions);
   if(done){QuestCopyFeedback.ApplyTemplate(use,B("#E4E7EB"));use.Foreground=B("#3E4954");}
   var check=new CheckBox{Style=(Style)FindResource("SemanticCheck"),IsChecked=done,IsEnabled=Editable && _workspace!.HasProgress(_workspace.SelectedGuide),VerticalAlignment=VerticalAlignment.Top,Foreground=B("#228842"),ToolTip=L("Отметить выполнение этапа","Mark step complete")};Grid.SetColumn(check,2);AutomationProperties.SetName(check,L("Выполнен этап ","Completed step ")+step.Order);
   check.Click+=(_,e)=>{e.Handled=true;if(Legacy(step) is {} legacy)_workspace!.SetStepDone(_workspace.SelectedGuide!,legacy,check.IsChecked==true);};grid.Children.Add(check);
   var card=new Border {Child=grid,Tag=step.Id,CornerRadius=new CornerRadius(5),Padding=new Thickness(10,9,10,9),Margin=new Thickness(0,0,0,5),BorderThickness=new Thickness(1),
    Background=B(done?(selected?"#EDF2F7":"#F6F8FB"):(selected?"#E9F2FF":"#FBFDFF")),BorderBrush=B(selected?(done?"#9AB3CF":"#6CA6F1"):"#D5E4F4"),Focusable=true,Cursor=Cursors.Hand};
   var accent=new Border {BorderBrush=selected?B("#2D78DB"):Brushes.Transparent,BorderThickness=new Thickness(2,0,0,0),Margin=new Thickness(-8,-7,0,-7),IsHitTestVisible=false};Grid.SetColumnSpan(accent,3);grid.Children.Add(accent);
   AutomationProperties.SetName(card,L("Этап ","Step ")+step.Order);
   card.MouseLeftButtonUp+=(_,e)=>{if(InteractiveAncestor(e.OriginalSource as DependencyObject,card))return;Select(step);e.Handled=true;};
   card.KeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space && ReferenceEquals(Keyboard.FocusedElement,card)){Select(step);e.Handled=true;}};
   card.GotKeyboardFocus+=(_,_)=>card.BorderBrush=B("#1764CC");card.LostKeyboardFocus+=(_,_)=>card.BorderBrush=B(selected?(done?"#9AB3CF":"#6CA6F1"):"#D5E4F4");
   _cards[step.Id]=card;StepsPanel.Children.Add(card);
  }
  WalkScroll.ScrollToVerticalOffset(offset);
 }
 private static bool InteractiveAncestor(DependencyObject? node,DependencyObject stop)
 {
  while(node is not null && node!=stop){if(node is Button or CheckBox)return true;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}return false;
 }
 private void Select(SemanticStep step){_selectedId=step.Id;DetailsTabs.SelectedIndex=0;RenderSteps();RenderDetail();}
 private Border Tag(string text,string color)=>new(){Background=B(color),BorderBrush=B(color),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3),Padding=new Thickness(6,1,6,1),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0),Child=new TextBlock {Text=text,Foreground=Brushes.White,FontSize=10,FontWeight=FontWeights.Bold}};
 private WrapPanel Coordinates(SemanticStep step,bool monochrome=false)
 {
  var panel=new WrapPanel{Margin=new Thickness(0,5,0,0)};
  foreach(var point in SemanticCoordinates.All(step))
  {
   var label=(point.Map is null?"":point.Map+" ")+$"{point.X},{point.Y}";
   var command=point.Map is null?$"{point.X} {point.Y}":$"/navi {point.Map} {point.X}/{point.Y}";
   var button=new Button{Content=label,Tag=new QuestNavigationTarget(point.Map??"",point.X,point.Y),FontSize=10,FontWeight=FontWeights.SemiBold,Padding=new Thickness(4,1,4,1),MinHeight=16,Height=20,Margin=new Thickness(0,0,5,0),ToolTip=string.Join(" · ",new[]{command,point.Role,point.Condition}.Where(v=>!string.IsNullOrWhiteSpace(v)))};
   button.Background=B(monochrome?"#EEF1F5":"#EFF5FE");button.Foreground=B(monochrome?"#4E5A66":"#1F477B");
   AutomationProperties.SetName(button,L("Скопировать координаты ","Copy coordinates ")+label);
   button.Click+=(_,e)=>{e.Handled=true;Notify(ClipboardService.TryCopy(command)?L("Координаты скопированы","Coordinates copied"):L("Не удалось скопировать","Copy failed"));};panel.Children.Add(button);
  }
  return panel;
 }
 private void ApplyText(TextBlock block,SemanticStep step,bool full,bool monochrome=false)
 {
  var text=full?step.Full(_language):step.Short(_language);var field=(full?"full_":"short_")+_language;
  var spans=new List<(int Start,int Length,string Type)>();
  foreach(var m in step.Mentions.Where(m=>m.Field==field))
   if(SemanticReadiness.Utf16Span(text,m.Start,m.End,m.Surface) is {} span)spans.Add((span.Start,m.Type=="map"?SemanticCoordinates.HighlightLength(text,span.Start,span.Length):span.Length,m.Type));
  // Exact answer text is linked by step_dialogues. This is not free-text answer discovery.
  foreach(var answer in step.Answers)
  {var index=text.IndexOf(answer.Text,StringComparison.Ordinal);if(index>=0 && answer.Text.Length>0 && !spans.Any(s=>s.Start<index+answer.Text.Length && s.Start+s.Length>index))spans.Add((index,answer.Text.Length,"answer"));}
  var offset=0;
  foreach(var span in spans.OrderBy(s=>s.Start))
  {
   if(span.Start<offset)continue;
   block.Inlines.Add(new Run(text[offset..span.Start]){Foreground=B(monochrome?"#5E6D80":"#0D3268")});
   block.Inlines.Add(new Run(text.Substring(span.Start,span.Length)){FontWeight=FontWeights.SemiBold,Foreground=B(monochrome?"#5E6D80":span.Type switch{"npc"=>"#075CB9","item"=>"#087747","map"=>"#985100","answer"=>"#783CAA",_=>"#0D3268"})});offset=span.Start+span.Length;
  }
  block.Inlines.Add(new Run(text[offset..]){Foreground=B(monochrome?"#5E6D80":"#0D3268")});if(monochrome)block.Foreground=B("#5E6D80");
 }
 private void RenderDetail()
 {
  DetailPanel.Children.Clear();AnswerPanel.Children.Clear();var step=_quest?.Steps.FirstOrDefault(s=>s.Id==_selectedId);if(step is null)return;
  var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  var prose=new StackPanel{Margin=new Thickness(0,0,12,0)};layout.Children.Add(prose);
  prose.Children.Add(Text(L("Этап ","Step ")+step.Order+(Done(step)?L(" ✓ Завершено"," ✓ Completed"):""),15,true));prose.Children.Add(Text(L("Полное описание","Full description"),18,true));
  var full=Text("",ActualWidth<1050?13:14);full.LineHeight=full.FontSize*1.67;ApplyText(full,step,true);prose.Children.Add(full);
  var aside=new StackPanel();Grid.SetColumn(aside,1);layout.Children.Add(aside);
  var imageHost=new Border {Background=B("#F1F4F8"),BorderBrush=B("#D5E0EC"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5),MinHeight=180,Padding=new Thickness(12),Child=Text(L("▧ Для этапа не добавлен скриншот","▧ No screenshot for this step"),11)};
  var image=step.Images.FirstOrDefault();
  if(image is not null)
  {
   // Only a source image explicitly attached to this stable step; never reuse the prototype illustration.
   Uri? uri=null;
   if(image.LocalPath is {} local)
   {var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"Data"))+Path.DirectorySeparatorChar;var path=Path.GetFullPath(Path.Combine(root,local));if(path.StartsWith(root,StringComparison.OrdinalIgnoreCase) && File.Exists(path))uri=new Uri(path);}
   if(uri is null && Uri.TryCreate(image.Url,UriKind.Absolute,out var remote) && remote.Scheme is "http" or "https")uri=remote;
   if(uri is not null)try{var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=uri;bitmap.DecodePixelWidth=600;bitmap.EndInit();imageHost.Child=new Image{Source=bitmap,Stretch=Stretch.Uniform,ToolTip=image.Caption};}catch(Exception ex){DebugLog.Write("Quest image: "+ex.Message);}
  }
  aside.Children.Add(imageHost);aside.Children.Add(Legend());
  var coordinates=new StackPanel();if(SemanticCoordinates.All(step).Count>0)coordinates.Children.Add(Coordinates(step));else coordinates.Children.Add(Text(L("Координаты для этого этапа не указаны в документе","No coordinates specified for this step"),11));
  if(step.Npcs.Count>0){var names=Text("NPC: "+string.Join(", ",step.Npcs),11,true);names.Foreground=B("#075CB9");coordinates.Children.Add(names);}
  aside.Children.Add(Info(L("Координаты","Coordinates"),coordinates));
  var materials=new StackPanel();var items=step.Items.Where(i=>i.Operation!="mention" && i.Operation!="decline").ToList();
  if(items.Count==0)materials.Children.Add(Text(L("Не требуются по описанию этапа","None specified for this step"),11));
  foreach(var item in items)
  {
   var line=Text(item.Name+(item.Quantity is {} n?" ×"+n:" ×?")+" · "+Operation(item.Operation),12,true);line.Foreground=B("#087747");materials.Children.Add(line);
   if(item.Condition is not null || item.Branch is not null)materials.Children.Add(Text(string.Join(" · ",new[]{item.Condition,item.Branch}.Where(x=>x is not null)),11));
  }
  aside.Children.Add(Info(L("Необходимые предметы","Required items"),materials));
  if(ActualWidth<760){layout.ColumnDefinitions[1].Width=new GridLength(0);layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetColumn(aside,0);Grid.SetRow(aside,1);}
  DetailPanel.Children.Add(layout);
  foreach(var answer in step.Answers)
  {var button=new Button{Content=answer.Text,FontSize=11,Height=24,MinHeight=24,Padding=new Thickness(7,0,7,0),Margin=new Thickness(0,0,5,5),ToolTip=string.Join(" · ",new[]{answer.Operation,answer.Condition,answer.Branch}.Where(x=>x is not null)),HorizontalAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>Notify(ClipboardService.TryCopy(answer.Text)?L("Текст скопирован","Text copied"):L("Ошибка копирования","Copy failed"));AnswerPanel.Children.Add(button);}
 }
 private Border Info(string title,UIElement content)
 {var stack=new StackPanel();stack.Children.Add(Text(title,12,true));stack.Children.Add(content);return new(){Child=stack,Background=B("#FAFCFF"),BorderBrush=B("#D6E4F3"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5),Padding=new Thickness(10),Margin=new Thickness(0,10,0,0)};}
 private WrapPanel Legend()
 {var legend=new WrapPanel{Margin=new Thickness(0,18,0,0)};foreach(var pair in new[]{(L("NPC","NPC"),"#075CB9"),(L("Предмет","Item"),"#087747"),(L("Карта / координаты","Map / coordinates"),"#985100"),(L("Вариант ответа","Answer"),"#783CAA")})legend.Children.Add(Tag(pair.Item1,pair.Item2));return legend;}
 private void RenderFacts()
 {
  FactsPanel.Children.Clear();if(_quest is null)return;
  var body=new StackPanel();body.Children.Add(Text(L("Требования и награды квеста","Quest requirements and rewards"),17,true));
  body.Children.Add(Text(_quest.Title,11));
  var columns=new Grid{Margin=new Thickness(0,12,0,0)};
  columns.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  var requirements=FactRows(_quest.Requirements.Where(f=>f.StepId is null).ToList());
  var left=Info(L("Суммарные материалы и требования","Total materials and requirements"),requirements);left.Margin=new Thickness(0,0,7,0);columns.Children.Add(left);
  var rewards=new StackPanel();
  var received=_quest.Rewards.Where(f=>f.Kind!="access").ToList();rewards.Children.Add(FactRows(received));
  var access=_quest.Rewards.Where(f=>f.Kind=="access").ToList();
  if(access.Count>0){rewards.Children.Add(Text(L("Доступ","Access"),15,true));rewards.Children.Add(FactRows(access));}
  var right=Info(L("Получаемые предметы и награды","Received items and rewards"),rewards);right.Margin=new Thickness(7,0,0,0);Grid.SetColumn(right,1);columns.Children.Add(right);body.Children.Add(columns);
  // Preserve source totals and branch conditions. Never add alternative branches together.
  foreach(var step in _quest.Steps)
  {
   var facts=_quest.Requirements.Where(f=>f.StepId==step.Id).ToList();
   if(facts.Count>0)body.Children.Add(Info(L("Материалы и требования — этап ","Materials and requirements — step ")+step.Order,FactRows(facts)));
   else
   {
    var materials=step.Items.Where(i=>i.Operation is "collect" or "bring" or "deliver" or "buy" or "use" or "consume").Select(i=>new SemanticFact(i.Id,"item",i.Name,i.Quantity?.ToString(),string.Join(" · ",new[]{Operation(i.Operation),i.Condition}.Where(v=>!string.IsNullOrWhiteSpace(v))),i.Branch,step.Id)).ToList();
    if(materials.Count>0)body.Children.Add(Info(L("Материалы — этап ","Materials — step ")+step.Order,FactRows(materials)));
   }
  }
  FactsPanel.Children.Add(Info("",body));
 }
 private StackPanel FactRows(IReadOnlyList<SemanticFact> facts)
 {
  var rows=new StackPanel();if(facts.Count==0)rows.Children.Add(Text(L("Нет данных","No data"),11));
  foreach(var fact in facts)
  {
   var row=new DockPanel();
   if(fact.Amount is not null){var amount=Text("×"+fact.Amount,12,true);amount.Margin=new Thickness(8,2,0,4);DockPanel.SetDock(amount,Dock.Right);row.Children.Add(amount);}
   var label=Text(fact.Name??FactLabel(fact.Kind),12,true);if(fact.Name is not null && fact.Kind.Contains("item"))label.Foreground=B("#087747");row.Children.Add(label);
   var content=new StackPanel();content.Children.Add(row);
   if(fact.Condition is not null)content.Children.Add(Text(fact.Condition,11));
   if(fact.Branch is not null)content.Children.Add(Text(L("Вариант: ","Branch: ")+fact.Branch,11));
   rows.Children.Add(new Border{Child=content,BorderBrush=B("#E2EAF4"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,7,0,5)});
  }
  return rows;
 }
 private string Operation(string value)=>value switch
 {"collect"=>L("собрать","collect"),"bring"=>L("принести","bring"),"deliver"=>L("передать","deliver"),"buy"=>L("купить","buy"),"use"=>L("использовать","use"),"consume"=>L("расходуется","consumed"),"possess"=>L("иметь при себе","possess"),"show"=>L("показать","show"),_=>value};
 private string FactLabel(string value)=>value switch
 {"base_level"=>L("Базовый уровень","Base level"),"job_level"=>L("Уровень профессии","Job level"),"base_exp"=>L("Базовый опыт","Base experience"),"job_exp"=>L("Опыт профессии","Job experience"),"item"=>L("Предмет","Item"),"item_consumed_total"=>L("Суммарные расходуемые материалы","Total consumed materials"),"item_not_consumed"=>L("Нерасходуемый предмет","Retained item"),"quest_completed"=>L("Предварительный квест","Prerequisite quest"),"zeny" or "zeny_total"=>L("Зени","Zeny"),_=>value.Replace('_',' ')};
 private void RenderSources()
 {
  SourcePanel.Children.Clear();if(_quest is null)return;
  SourcePanel.Children.Add(Text(L("Источники","Sources"),17,true));var source=_quest.Source;
  var link=new Button{Content=source.Url,HorizontalAlignment=HorizontalAlignment.Left,ToolTip=source.Url};link.Click+=(_,_)=>OpenUrl(source.Url);SourcePanel.Children.Add(link);
  SourcePanel.Children.Add(Text(source.Kind+"\n"+source.Revision+"\n"+source.License+"\n"+_quest.Review+"\n"+L("Проверка сервера: ","Server verification: ")+_quest.Availability));SourcePanel.Children.Add(Legend());
  SourcePanel.Children.Add(Text(L("Неизвестные карты и координаты не заполняются автоматически.","Unknown maps and coordinates are never inferred."),11));
 }
 private static void OpenUrl(string value){if(Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https")try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(Exception ex){DebugLog.Write(ex.Message);}}
 internal void ResetSemanticChoice()=>_semanticChoice=null;
 private void Ready_Changed(object sender,SelectionChangedEventArgs e)
 {
  if(_rendering || ReadyChoice.SelectedItem is not SemanticQuest quest || _workspace is null)return;
  _semanticChoice=null;
  if(_workspace.Catalog?.Guides.GetValueOrDefault(quest.Id) is {} guide)GuideRequested?.Invoke(guide);
  else {_semanticChoice=quest.Id;Render(_workspace);} // New ready content remains readable before legacy progress is reconciled.
 }
 private void Ru_Click(object sender,RoutedEventArgs e)=>Language("ru");private void En_Click(object sender,RoutedEventArgs e)=>Language("en");
 private void Language(string language){if(_workspace is null)return;var offset=WalkScroll.VerticalOffset;_workspace.Settings.Language=language;LocalizationService.ApplyLanguage(language);_workspace.RefreshLanguage();Render(_workspace);WalkScroll.ScrollToVerticalOffset(offset);Notify(L("Язык изменён","Language changed"));}
 private void Add_Click(object sender,RoutedEventArgs e){if(Editable && _workspace?.SelectedGuide is {} guide){_workspace.ToggleTracking(guide);Notify(L("Отслеживание обновлено","Tracking updated"));}}
 private void Current_Click(object sender,RoutedEventArgs e)=>ShowCurrentStep();
 public void ShowCurrentStep(){DetailsTabs.SelectedIndex=0;if(Current() is {} step)_selectedId=step.Id;RenderSteps();RenderDetail();Dispatcher.BeginInvoke(ScrollToCurrent,DispatcherPriority.ContextIdle);}
 public void ScrollToCurrent(){if(Current() is {} step && _cards.TryGetValue(step.Id,out var card)){WalkScroll.UpdateLayout();WalkScroll.ScrollToVerticalOffset(card.TranslatePoint(new Point(),StepsPanel).Y);}}
 internal void ShowFacts()=>DetailsTabs.SelectedIndex=1;
 internal void ValidateSelectionSmoke()
 {
  if(_quest is null || _workspace is null)throw new InvalidOperationException("Semantic view not loaded.");
  // A newly ready quest must remain readable even before the old progress catalog knows it.
  var fresh=new QuestDetailsControl{_catalog=_catalog};
  fresh.Render(new QuestWorkspace(new AppSettings(),catalog:new QuestCatalog([])));
  fresh.ReadyChoice.SelectedItem=fresh.ReadyChoice.Items.Cast<SemanticQuest>().First(q=>q.Id==_quest.Id);
  if(fresh._quest?.Id!=_quest.Id || fresh.Current() is not null || fresh.Editable)
   throw new InvalidOperationException("New ready content inherited another quest's progress or remained hidden.");
  if(Metadata.Visibility!=Visibility.Collapsed || Quality.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Walkthrough metadata must stay hidden.");
  var current=Current()?.Id;var selected=_selectedId;
  var other=_quest.Steps.First(s=>s.Id!=current);Select(other);
  if(AnswerPanel.Children.Count!=other.Answers.Count || Grid.GetRow((UIElement)AnswerPanel.Parent)!=1)
   throw new InvalidOperationException("Answer actions must stay in the detail footer.");
  if(_cards.TryGetValue(other.Id,out var selectedCard))
  {
   var pointButtons=((Grid)selectedCard.Child).Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<WrapPanel>()).SelectMany(p=>p.Children.OfType<Button>()).Count(b=>b.Tag is QuestNavigationTarget);
   if(pointButtons!=SemanticCoordinates.All(other).Count)throw new InvalidOperationException("Not all explicit coordinates have navigation buttons.");
  }
  if(Current()?.Id!=current || _selectedId!=other.Id)throw new InvalidOperationException("Reading a semantic step changed current progress.");
  ShowFacts();Render(_workspace);
  if(DetailsTabs.SelectedIndex!=1 || _selectedId!=other.Id)throw new InvalidOperationException("Facts tab lost selection.");
  _selectedId=selected;ShowCurrentStep();
 }

 internal bool HasStepCount(int count)=>_quest?.Steps.Count==count && _cards.Count==count;
 internal Button? CurrentNavigationButton=>Current() is {} step && _cards.TryGetValue(step.Id,out var card)?((Grid)card.Child).Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<WrapPanel>()).SelectMany(p=>p.Children.OfType<Button>()).FirstOrDefault(b=>b.Tag is QuestNavigationTarget):null;
 internal bool CurrentStepInView
 {get{if(DetailsTabs.SelectedIndex!=0 || Current() is not {} step || !_cards.TryGetValue(step.Id,out var card))return false;var top=card.TranslatePoint(new Point(),StepsPanel).Y-WalkScroll.VerticalOffset;return top>=-1 && top<WalkScroll.ViewportHeight;}}
 internal void ReportSave(bool success)=>Notify(success?L("Прогресс сохранён","Progress saved"):L("Не удалось сохранить прогресс","Progress could not be saved"));
 internal bool CopyToastVisible=>Toast.Visibility==Visibility.Visible;

 private void Notify(string message){ToastText.Text=message;Toast.Visibility=Visibility.Visible;_toast.Stop();_toast.Start();}
 private void Layout_Changed(object sender,SizeChangedEventArgs e)
 {
  var narrow=ActualWidth<760;Grid.SetColumn(RightPanel,narrow?0:1);Grid.SetRow(RightPanel,narrow?1:0);RightColumn.Width=narrow?new GridLength(0):new GridLength(1,GridUnitType.Star);
  TopRow.Height=narrow?GridLength.Auto:new GridLength(1,GridUnitType.Star);BottomRow.Height=narrow?new GridLength(1,GridUnitType.Star):new GridLength(0);LeftPanel.MaxHeight=narrow?385:double.PositiveInfinity;
  LeftPanel.Margin=narrow?new Thickness(0,0,0,5):new Thickness(0,0,5,0);RightPanel.Margin=narrow?new Thickness(0,5,0,0):new Thickness(5,0,0,0);RenderDetail();
 }
}
