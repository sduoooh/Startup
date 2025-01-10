using System;
using System.ComponentModel;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;

namespace startup
{
    public enum ProcessorRequestType
    {
        PluginRemain,
        PluginChange,
        PluginOff,
        Close
    }

    public class ProcessorRequest
    {
        public ProcessorRequestType Type;
        public string PluginName;

        public ProcessorRequest(ProcessorRequestType type, string pluginName)
        {
            Type = type;
            PluginName = pluginName;
        }
    }


    internal interface IProcessor
    {
        Action<ProcessorRequest> OnProcessSelected { get; set; }

        string[] InputChangeProcess(string input);
        ProcessorRequest Selected(string content);
        bool GetWorkStatus();
        void Reset();
    }

    public class Repeater : INotifyPropertyChanged, IDisposable
    {
        private IProcessor _subscriptor = new RP();
        private string _inputText = "";
        private int _selectedIndex = -1;
        private Visibility _inputBoxVisible;
        private Visibility _labelVisibility;
        private String[] _wordList;
        private Visibility _listVisibility;
        private String _pluginName;
        private CancellationTokenSource _suggestionCts;
        private readonly int _debounceMs = 50;
        private Task _currentSuggestionTask;

        public string[] WordList
        {
            get
            {
                return _wordList;
            }
            set
            {
                if (_wordList != value)
                {
                    _wordList = value;
                    OnPropertyChanged(nameof(WordList));
                }
            }
        }
        public Visibility ListVisible
        {
            get
            {
                return _listVisibility;
            }
            set
            {
                if (value != _listVisibility)
                {
                    _listVisibility = value;
                    OnPropertyChanged(nameof(ListVisible));
                }
            }
        }
        public string PluginName
        {
            get { return _pluginName; }
            set
            {
                if (value != _pluginName)
                {
                    _pluginName = value;
                    OnPropertyChanged(nameof(PluginName));
                }
            }
        }
        public Visibility LabelVisibility
        {
            get { return _labelVisibility; }
            set
            {
                if (_labelVisibility != value)
                {
                    _labelVisibility = value;
                    OnPropertyChanged(nameof(LabelVisibility));
                }

            }
        }
        public Visibility InputBoxVisible
        {
            get { return _inputBoxVisible; }
            set
            {
                if (_inputBoxVisible != value)
                {
                    _inputBoxVisible = value;
                    OnPropertyChanged(nameof(InputBoxVisible));
                }
            }
        }

        public Repeater()
        {
            _subscriptor.OnProcessSelected += ProcessSelected;
            this.WordList = Array.Empty<string>();
            this.PluginName = string.Empty;
            this.ListVisible = Visibility.Collapsed;
            this.LabelVisibility = Visibility.Collapsed;
            this.InputBoxVisible = Visibility.Collapsed;
        }
        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                if (_selectedIndex != value)
                {
                    _selectedIndex = value;
                    OnPropertyChanged(nameof(SelectedIndex));
                    OnPropertyChanged(nameof(InputText));
                }
            }
        }

        public string InputText
        {
            get
            {
                if (SelectedIndex == -1) return _inputText;
                else return WordList[SelectedIndex];
            }
            set
            {
                if ((SelectedIndex == -1 && _inputText != value) || 
                    (SelectedIndex != -1 && value != WordList[SelectedIndex]))
                {
                    _selectedIndex = -1;
                    OnPropertyChanged(nameof(SelectedIndex));

                    _inputText = value;
                    OnPropertyChanged(nameof(InputText));

                    _suggestionCts?.Cancel();
                    _suggestionCts = new CancellationTokenSource();
                    
                    UpdateSuggestions(_suggestionCts.Token);
                }
            }
        }

        public void PluginOff()
        {
            PluginName = String.Empty;
            LabelVisibility = Visibility.Collapsed;
        }

        public void PluginOn(string name)
        {
            PluginName = name;
            LabelVisibility = Visibility.Visible;   
        }

        public void InputClear()
        {
            InputText = string.Empty;
            WordList = Array.Empty<string>();
            ListVisible = Visibility.Collapsed;
            SelectedIndex = -1;

            OnPropertyChanged(nameof(InputText));
            OnPropertyChanged(nameof(WordList));
            OnPropertyChanged(nameof(ListVisible));
            OnPropertyChanged(nameof(SelectedIndex));
        }

        public void InputReset()
        {
            InputClear();
            PluginOff();
            _subscriptor.Reset();
        }

        public bool GetWorkStatus()
        {
            return _subscriptor.GetWorkStatus();
        }

        public void ProcessSelected(ProcessorRequest request)
        {
            switch (request.Type)
            {
                case ProcessorRequestType.PluginRemain:
                    InputClear();
                    break;
                case ProcessorRequestType.PluginChange:
                    InputClear();
                    PluginOn(request.PluginName);
                    break;
                case ProcessorRequestType.PluginOff:
                    InputReset();
                    break;
                case ProcessorRequestType.Close:
                    InputReset();
                    InputBoxVisible = Visibility.Collapsed;
                    break;
            };
        }

        public void Selected()
        {
            ProcessSelected(_subscriptor.Selected(InputText));
        }

        private string[] InputChangeProcess()
        {
            return _subscriptor.InputChangeProcess(InputText);
        }

        private async void UpdateSuggestions(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_debounceMs, cancellationToken);

                var suggestions = await Task.Run(() => 
                    InputChangeProcess(), cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    WordList = suggestions;
                    ListVisible = suggestions.Length > 0 ? 
                        Visibility.Visible : Visibility.Collapsed;
                    
                    OnPropertyChanged(nameof(WordList));
                    OnPropertyChanged(nameof(ListVisible));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WordList = Array.Empty<string>();
                ListVisible = Visibility.Collapsed;
            }
        }

        public void Dispose()
        {
            _suggestionCts?.Cancel();
            _suggestionCts?.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
