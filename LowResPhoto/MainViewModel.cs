using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Threading;
using System.Windows.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;
using LowResPhoto.Properties;
using System.Reflection;
using System.Windows;
using PhotoMetadata;

namespace LowResPhoto
{
    public class NotifiableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public class MainViewModel : NotifiableBase
    {
        public event EventHandler<WorkItemCompletedEventArgs> OnWorkItemCompleted;

        public MainViewModel()
        {
            PersistAllSettings(PersistDirection.Load);
            _uiDispatcher = Dispatcher.CurrentDispatcher;
        }

        #region Persistence
        private PropertyInfo[] _persistedSettings;
        public PropertyInfo[] PersistedSettings
        {
            get
            {
                if (_persistedSettings == null)
                    _persistedSettings = Settings.Default.GetType().GetProperties().Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(System.Configuration.UserScopedSettingAttribute))).ToArray();

                return _persistedSettings;
            }
        }

        private PropertyInfo[] _myProps;
        public PropertyInfo[] MyProps
        {
            get
            {
                if (_myProps == null)
                    _myProps = typeof(MainViewModel).GetProperties();

                return _myProps;
            }
        }

        private void PersistAllSettings(PersistDirection direction)
        {
            foreach (var setting in PersistedSettings)
            {
                var vmProp = MyProps.FirstOrDefault(x => x.Name.Equals(setting.Name, StringComparison.InvariantCultureIgnoreCase));
                if (vmProp != null)
                {
                    switch (direction)
                    {
                        case PersistDirection.Load:
                            vmProp.SetValue(this, setting.GetValue(Settings.Default));
                            break;
                        case PersistDirection.Save:
                            setting.SetValue(Settings.Default, vmProp.GetValue(this));
                            break;
                    }
                }
            }
            if (direction == PersistDirection.Save)
                Settings.Default.Save();
        }
        #endregion

        private int _WindowHeight;
        public int WindowHeight
        {
            get { return _WindowHeight; }
            set { _WindowHeight = value; NotifyPropertyChanged(nameof(WindowHeight)); }
        }

        private int _WindowWidth;
        public int WindowWidth
        {
            get { return _WindowWidth; }
            set { _WindowWidth = value; NotifyPropertyChanged(nameof(WindowWidth)); }
        }

        private string _HighResFolder;
        public string HighResFolder
        {
            get { return _HighResFolder; }
            set { _HighResFolder = value; NotifyPropertyChanged(nameof(HighResFolder)); }
        }

        private string _LowResFolder;
        public string LowResFolder
        {
            get { return _LowResFolder; }
            set { _LowResFolder = value; NotifyPropertyChanged(nameof(LowResFolder)); }
        }

        private string _NConvertFolder;
        public string NConvertFolder
        {
            get { return _NConvertFolder; }
            set { _NConvertFolder = value; NotifyPropertyChanged(nameof(NConvertFolder)); }
        }

        private bool _SkipExisting;
        public bool SkipExisting
        {
            get { return _SkipExisting; }
            set { _SkipExisting = value; NotifyPropertyChanged(nameof(SkipExisting)); }
        }

        private bool _RetrieveMeta;
        public bool RetrieveMeta
        {
            get { return _RetrieveMeta; }
            set
            {
                _RetrieveMeta = value;
                NotifyPropertyChanged(nameof(RetrieveMeta));
                MetaDbVisibility = _RetrieveMeta ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private bool _AutoScroll;
        public bool AutoScroll
        {
            get { return _AutoScroll; }
            set { _AutoScroll = value; NotifyPropertyChanged(nameof(AutoScroll)); }
        }

        private int _Concurrency;
        public int Concurrency
        {
            get { return _Concurrency; }
            set { _Concurrency = value; NotifyPropertyChanged(nameof(Concurrency)); }
        }

        private int _LongSize;
        public int LongSize
        {
            get { return _LongSize; }
            set { _LongSize = value; NotifyPropertyChanged(nameof(LongSize)); }
        }

        private Visibility _MetaDbVisibility;
        public Visibility MetaDbVisibility
        {
            get { return _MetaDbVisibility; }
            set { _MetaDbVisibility = value; NotifyPropertyChanged(nameof(MetaDbVisibility)); }
        }

        private string _TimeRunning;
        public string TimeRunning
        {
            get { return _TimeRunning; }
            set { _TimeRunning = value; NotifyPropertyChanged(nameof(TimeRunning)); }
        }

        public ObservableCollection<ConvertFolder> Folders { get; } = new ObservableCollection<ConvertFolder>();

        public ObservableCollection<LogItem> Logs { get; } = new ObservableCollection<LogItem>();

        private ICommand _SyncCommand;
        public ICommand SyncCommand
        {
            get
            {
                if (_SyncCommand == null)
                    _SyncCommand = new RelayCommand(x => DoSync(), x => CanSync());
                return _SyncCommand;
            }
        }

        private const string CaptionSync = "_Sync";
        private const string CaptionCancel = "_Cancel";

        private string _SyncCaption = CaptionSync;
        public string SyncCaption
        {
            get { return _SyncCaption; }
            set { _SyncCaption = value; NotifyPropertyChanged(nameof(SyncCaption)); }
        }

        private Dispatcher _uiDispatcher;
        private bool _isCanceling;
        private bool _hasAnalyzeDone;
        private bool _hasScheduleDone;
        private DateTime _startTime;
        private Timer _runningTimer;
        private ConcurrentQueue<WorkItem> _workQueue;
        private int _currentRunningCount;

        private void SetRunningTime(object stateInfo)
        {
            TimeRunning = (DateTime.Now - _startTime).ToString(@"hh\:mm\:ss");
        }

        private bool _isSyncing;
        public bool IsSyncing
        {
            get { return _isSyncing; }
            set
            {
                _isSyncing = value;
                NotifyPropertyChanged(nameof(IsSyncing));
                if (_isSyncing)
                {
                    SyncCaption = CaptionCancel;
                    _startTime = DateTime.Now;
                    _runningTimer = new Timer(new TimerCallback(SetRunningTime));
                    _runningTimer.Change(0, 1000);
                }
                else
                {
                    SyncCaption = CaptionSync;
                    _runningTimer?.Change(0, -1);
                }
            }
        }

        private void DoSync()
        {
            if (IsSyncing)
            {
                _isCanceling = true;
                IsSyncing = false;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Folders.Where(x => x.Status == ConvertStatus.Working).ToList().ForEach(x => x.Status = ConvertStatus.Cancelled);
                }), DispatcherPriority.ApplicationIdle);
                AddLog(LogCategory.Info, $"Sync stopped at {DateTime.Now.ToString("T")}");
            }
            else
            {
                AddLog(LogCategory.Info, $"Sync started at {DateTime.Now.ToString("T")}");
                HighResFolder = HighResFolder.TrimEnd('\\');
                LowResFolder = LowResFolder.TrimEnd('\\');
                IsSyncing = true;
                _hasAnalyzeDone = false;
                _isCanceling = false;
                _hasScheduleDone = false;
                Folders.Clear();
                ThreadPool.QueueUserWorkItem(x =>
                {
                    AnalyseFolder(new DirectoryInfo(HighResFolder));
                    _hasAnalyzeDone = true;
                });
                ThreadPool.QueueUserWorkItem(x => ScheduleWork());
                ThreadPool.QueueUserWorkItem(x => RunWork());
                if (RetrieveMeta)
                    ThreadPool.QueueUserWorkItem(x => SaveMeta());
            }
        }

        private bool CanSync()
        {
            return true;
        }

        private void ScheduleWork()
        {
            while ((Folders.Count == 0 && !_hasAnalyzeDone))
            {
                Thread.Sleep(300);
            }
            if (Folders.Count == 0)
            {
                IsSyncing = false;
                return;
            }
            var q = from photo in MetaContext.Photos select photo.FullPath;
            _existingMeta = q.ToList();
            _workQueue = new ConcurrentQueue<WorkItem>();
            _photoQueue = new ConcurrentQueue<Photo>();
            AddedRecordCount = 0;
            ConvertFolder currentFolder;
            while (!_isCanceling && (currentFolder = GetNextFolder()) != null)
            {
                while (_workQueue.Count > 200)
                {
                    Thread.Sleep(200);
                }
                currentFolder.Status = ConvertStatus.Working;
                var targetFolder = new DirectoryInfo(currentFolder.Path.Replace(HighResFolder, LowResFolder));
                if (!targetFolder.Exists)
                    targetFolder.Create();
                else
                {
                    DeleteTargetOnlyFolders(currentFolder, targetFolder);
                    DeleteTargetOnlyFiles(currentFolder, targetFolder);
                }
                if (_isCanceling)
                {
                    currentFolder.Status = ConvertStatus.Cancelled;
                    return;
                }
                foreach (var file in currentFolder.JpegFiles)
                {
                    _workQueue.Enqueue(new WorkItem() { Folder = currentFolder, File = file });
                }
            }
            _hasScheduleDone = true;
        }

        private static void DeleteTargetOnlyFiles(ConvertFolder sourceFolder, DirectoryInfo targetFolder)
        {
            var existingFiles = targetFolder.GetFiles();
            var toDelete = existingFiles.Where(ef => !sourceFolder.JpegFiles.Any(x => x.Name.Equals(ef.Name, StringComparison.InvariantCultureIgnoreCase))).ToList();
            sourceFolder.CountDeleteFile = toDelete.Count;
            foreach (var delFile in toDelete)
            {
                delFile.Delete();
            }
        }

        public static void DeleteTargetOnlyFolders(ConvertFolder sourceFolder, DirectoryInfo targetFolder)
        {
            var targetFolders = targetFolder.GetDirectories();
            var sourceDirectory = new DirectoryInfo(sourceFolder.Path);
            var sourceFolders = sourceDirectory.GetDirectories();
            var toDelete = targetFolders.Where(folder => !sourceFolders.Any(x => x.Name.Equals(folder.Name, StringComparison.InvariantCultureIgnoreCase))).ToList();
            sourceFolder.CountDeleteFolder = toDelete.Count;
            foreach (var folder in toDelete)
            {
                folder.Delete(true);
            }
        }

        private bool ShouldRunWork
        {
            get
            {
                return !_isCanceling && (_workQueue == null || _workQueue.Any() || !_hasScheduleDone);
            }
        }

        private void RunWork()
        {
            Thread.Sleep(500);
            while (ShouldRunWork)
            {
                while (_workQueue == null || _currentRunningCount >= Concurrency || (!_workQueue.Any() && !_hasScheduleDone))
                    Thread.Sleep(50);

                WorkItem wi;
                if (!_workQueue.TryDequeue(out wi))
                    continue;

                bool toCopy = true, toMeta = true;
                var targetFI = new FileInfo(wi.File.FullName.Replace(HighResFolder, LowResFolder));
                if (targetFI.Exists && SkipExisting)
                    toCopy = false;
                if (!RetrieveMeta || (SkipExisting && _existingMeta.Any(x => string.Equals(x, wi.File.FullName, StringComparison.InvariantCultureIgnoreCase))))
                    toMeta = false;

                if (!toCopy && !toMeta)
                {
                    AddOneDone(wi, false, false);
                    continue;
                }
                Interlocked.Increment(ref _currentRunningCount);
                Task.Factory.StartNew(() =>
                {
                    if (toMeta)
                        DoRetrieveMeta(wi.File);

                    if (toCopy)
                        ConvertFile(wi.File, targetFI);

                    AddOneDone(wi, toCopy, toMeta);
                    Interlocked.Decrement(ref _currentRunningCount);
                });
            }
            IsSyncing = false;
            AddLog(LogCategory.Info, $"Sync completed at {DateTime.Now.ToString("T")}");
        }

        private void AddOneDone(WorkItem wi, bool isCopied, bool isMeta)
        {
            lock (wi.Folder)
            {
                if (isCopied)
                    wi.Folder.CountCopied++;
                else
                    wi.Folder.CountSkipped++;

                if (isMeta)
                    wi.Folder.CountMeta++;

                if (wi.Folder.CountAll <= wi.Folder.CountDone)
                    wi.Folder.Status = ConvertStatus.Done;
            }
            if (AutoScroll)
                OnWorkItemCompleted?.Invoke(this, new WorkItemCompletedEventArgs() { Folder = wi.Folder });
        }

        #region Meta

        private int _AddedRecordCount;
        public int AddedRecordCount
        {
            get { return _AddedRecordCount; }
            set { _AddedRecordCount = value; NotifyPropertyChanged(nameof(AddedRecordCount)); }
        }

        private int _TotalRecordCount;
        public int TotalRecordCount
        {
            get { return _TotalRecordCount; }
            set { _TotalRecordCount = value; NotifyPropertyChanged(nameof(TotalRecordCount)); }
        }

        private PhotoContext _metaContext;
        public PhotoContext MetaContext
        {
            get
            {
                if (_metaContext == null)
                    _metaContext = new PhotoContext();
                return _metaContext;
            }
        }

        private ConcurrentQueue<Photo> _photoQueue;
        private List<Photo> _pendingMetaQueue = new List<Photo>();
        private const int MaxSave = 50;
        private List<string> _existingMeta;

        private void SaveMeta()
        {
            while (ShouldRunWork)
            {
                while (_photoQueue == null || (!_photoQueue.Any() && !_hasScheduleDone))
                    Thread.Sleep(200);

                Photo photo;
                if (!_photoQueue.TryDequeue(out photo))
                    continue;

                var existing = MetaContext.Photos.FirstOrDefault(x => x.FullPath == photo.FullPath);
                if (existing == null)
                {
                    _pendingMetaQueue.Add(photo);
                    MetaContext.Photos.Add(photo);
                }
                if (_pendingMetaQueue.Count >= MaxSave)
                    SaveDB();
            }
            SaveDB();
        }

        private void SaveDB()
        {
            try
            {
                var count = MetaContext.SaveChanges();
                AddedRecordCount += count;
                TotalRecordCount = MetaContext.Photos.Count();
                _pendingMetaQueue.Clear();
            }
            catch (Exception ex)
            {
                AddLog(LogCategory.Error, ex.ToString());
            }
        }

        private void DoRetrieveMeta(FileInfo fi)
        {
            _photoQueue.Enqueue(MetaRetriever.RetrieveFromFile(fi));
        }

        #endregion

        #region Convert
        public string NConvertExe
        {
            get { return NConvertFolder + @"\nconvert.exe"; }
        }

        private void ConvertFile(FileInfo sourceFI, FileInfo targetFI)
        {
            if (_isCanceling)
                return;

            if (targetFI.Exists)
            {
                targetFI.Delete();
            }
            var psi = new ProcessStartInfo(NConvertExe, $"-out jpeg -resize longest {LongSize} -o \"{targetFI.FullName}\" \"{sourceFI.FullName}\"") { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, UseShellExecute = false };
            var proc = Process.Start(psi);
            proc.WaitForExit(5000);
        }
        #endregion

        private ConvertFolder GetNextFolder()
        {
            return Folders.FirstOrDefault(x => x.Status == ConvertStatus.Pending);
        }

        private void AnalyseFolder(DirectoryInfo di)
        {
            var dirs = di.GetDirectories();
            var files = di.GetFiles();
            if (files.Length > 0)
            {
                var jpgFiles = files.Where(x => x.Extension.Equals(".jpg", StringComparison.InvariantCultureIgnoreCase)).ToList();
                if (jpgFiles.Any())
                {
                    var cf = new ConvertFolder() { CountAll = jpgFiles.Count, Path = di.FullName, Status = ConvertStatus.Pending, JpegFiles = jpgFiles };
                    _uiDispatcher.BeginInvoke(new Action(() =>
                    {
                        Folders.Add(cf);
                    }));
                }
            }
            if (dirs.Length > 0)
            {
                foreach (var dir in dirs)
                {
                    AnalyseFolder(dir);
                }
            }
        }

        public void SaveSetting()
        {
            PersistAllSettings(PersistDirection.Save);
        }

        private void AddLog(LogCategory logCategory, string message)
        {
            _uiDispatcher.BeginInvoke(new Action(() =>
            {
                Logs.Insert(0, new LogItem() { Category = logCategory, Message = message });
            }));
        }
    }

    public class ConvertFolder : NotifiableBase
    {
        private ConvertStatus _Status;
        public ConvertStatus Status
        {
            get { return _Status; }
            set { _Status = value; NotifyPropertyChanged(nameof(Status)); }
        }

        private string _Path;
        public string Path
        {
            get { return _Path; }
            set { _Path = value; NotifyPropertyChanged(nameof(Path)); }
        }

        private int _CountAll;
        public int CountAll
        {
            get { return _CountAll; }
            set { _CountAll = value; NotifyPropertyChanged(nameof(CountAll)); }
        }

        public int CountDone
        {
            get { return CountSkipped + CountCopied; }
        }

        private int _CountSkipped;
        public int CountSkipped
        {
            get { return _CountSkipped; }
            set { _CountSkipped = value; NotifyPropertyChanged(nameof(CountSkipped)); }
        }

        private int _CountCopied;
        public int CountCopied
        {
            get { return _CountCopied; }
            set { _CountCopied = value; NotifyPropertyChanged(nameof(CountCopied)); }
        }

        public string CountDelete
        {
            get
            {
                if (CountDeleteFile == 0 && CountDeleteFolder == 0)
                    return "0";
                return $"{CountDeleteFile} files/{CountDeleteFolder} folders";
            }
        }

        private int _CountDeleteFile;
        public int CountDeleteFile
        {
            get { return _CountDeleteFile; }
            set { _CountDeleteFile = value; NotifyPropertyChanged(nameof(CountDelete)); }
        }

        private int _CountDeleteFolder;
        public int CountDeleteFolder
        {
            get { return _CountDeleteFolder; }
            set { _CountDeleteFolder = value; NotifyPropertyChanged(nameof(CountDelete)); }
        }

        private int _CountMeta;
        public int CountMeta
        {
            get { return _CountMeta; }
            set { _CountMeta = value; NotifyPropertyChanged(nameof(CountMeta)); }
        }

        public List<FileInfo> JpegFiles { get; set; }
    }

    public class WorkItem
    {
        public FileInfo File { get; set; }
        public ConvertFolder Folder { get; set; }
    }

    public enum ConvertStatus
    {
        Pending,
        Working,
        Done,
        Skipped,
        Error,
        Cancelled
    }

    public enum PersistDirection
    {
        Load,
        Save
    }

    public class WorkItemCompletedEventArgs : EventArgs
    {
        public ConvertFolder Folder { get; set; }
    }

    public enum LogCategory
    {
        Info,
        Error
    }

    public class LogItem
    {
        public LogCategory Category { get; set; }
        public string Message { get; set; }
    }
}
