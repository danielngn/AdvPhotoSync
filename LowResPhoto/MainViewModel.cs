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
        public MainViewModel()
        {
            PersistAllSettings(PersistDirection.Load);
        }

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
                    switch(direction)
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
            if(direction == PersistDirection.Save)
                Settings.Default.Save();
        }

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

        private string _SyncCaption = "Sync";
        public string SyncCaption
        {
            get { return _SyncCaption; }
            set { _SyncCaption = value; NotifyPropertyChanged(nameof(SyncCaption)); }
        }

        private Dispatcher _uiDispatcher;
        private bool _isCanceling;
        private bool _hasAnalyzeDone;
        private bool _hasScheduleDone;
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
                    SyncCaption = "Cancel";
                }
                else
                {
                    SyncCaption = "Sync";
                }
            }
        }

        private void DoSync()
        {
            if (IsSyncing)
            {
                _isCanceling = true;
            }
            else
            {
                HighResFolder = HighResFolder.TrimEnd('\\');
                LowResFolder = LowResFolder.TrimEnd('\\');
                IsSyncing = true;
                _hasAnalyzeDone = false;
                _hasScheduleDone = false;
                _uiDispatcher = Dispatcher.CurrentDispatcher;
                Folders.Clear();
                ThreadPool.QueueUserWorkItem(x =>
                {
                    AnalyseFolder(new DirectoryInfo(HighResFolder));
                    _hasAnalyzeDone = true;
                });
                ThreadPool.QueueUserWorkItem(x => ScheduleWork());
                ThreadPool.QueueUserWorkItem(x => RunWork());
            }
        }

        private ConcurrentQueue<WorkItem> _workQueue;

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
            _workQueue = new ConcurrentQueue<WorkItem>();
            ConvertFolder currentFolder;
            while (!_isCanceling && (currentFolder = GetNextFolder()) != null)
            {
                while (_workQueue.Count > 200)
                {
                    Thread.Sleep(300);
                }
                currentFolder.Status = ConvertStatus.Working;
                var targetFolder = new DirectoryInfo(currentFolder.Path.Replace(HighResFolder, LowResFolder));
                if (!targetFolder.Exists)
                    targetFolder.Create();
                else
                {
                    DeleteTargetOnlyFiles(currentFolder, targetFolder);
                }
                if (_isCanceling)
                    return;
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
            sourceFolder.CountDelete = toDelete.Count;
            foreach (var delFile in toDelete)
            {
                delFile.Delete();
            }
        }

        private int _currentRunningCount;

        private void RunWork()
        {
            while (!_isCanceling && (_workQueue == null || _workQueue.Any() || !_hasScheduleDone))
            {
                while (_workQueue == null || _currentRunningCount >= Concurrency || (!_workQueue.Any() && !_hasScheduleDone))
                    Thread.Sleep(200);

                WorkItem wi;
                if (!_workQueue.TryDequeue(out wi))
                    continue;
                var targetFI = new FileInfo(wi.File.FullName.Replace(HighResFolder, LowResFolder));
                if (targetFI.Exists && SkipExisting)
                {
                    AddOneDone(wi);
                    continue;
                }
                else
                {
                    Interlocked.Increment(ref _currentRunningCount);
                    Task.Factory.StartNew(() =>
                    {
                        ConvertFile(wi.File, targetFI);
                        AddOneDone(wi);
                        Interlocked.Decrement(ref _currentRunningCount);
                    });
                }
            }
            IsSyncing = false;
        }

        private void AddOneDone(WorkItem wi)
        {
            lock (wi.Folder)
            {
                wi.Folder.CountDone++;
                if (wi.Folder.CountAll <= wi.Folder.CountDone)
                    wi.Folder.Status = ConvertStatus.Done;
            }
        }

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

        private bool CanSync()
        {
            return true;
        }

        public ObservableCollection<ConvertFolder> Folders { get; } = new ObservableCollection<ConvertFolder>();

        public void SaveSetting()
        {
            PersistAllSettings(PersistDirection.Save);
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

        private int _CountDone;
        public int CountDone
        {
            get { return _CountDone; }
            set { _CountDone = value; NotifyPropertyChanged(nameof(CountDone)); }
        }

        private int _CountDelete;
        public int CountDelete
        {
            get { return _CountDelete; }
            set { _CountDelete = value; NotifyPropertyChanged(nameof(CountDelete)); }
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
        Error
    }

    public enum PersistDirection
    {
        Load,
        Save
    }

}
