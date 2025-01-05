using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;

namespace startup
{
    public enum NextProcessor
    {
        Self,
        Next,
        None
    }

    public interface IPlugin
    {
        string[] InputChangeProcess(string input);
        PluginRequest Selected(string input);
        string GetName();
        void Stop();
        void Start(Action<PluginRequest> callback);
    }

    public class PluginRequest
    {
        public NextProcessor nextProcessor;
        public ProcessorRequest processorRequest;
        public IPlugin newPlugin;

        public PluginRequest(NextProcessor nextProcessor, ProcessorRequest processorRequest, IPlugin newPlugin = null)
        {
            this.nextProcessor = nextProcessor;
            this.processorRequest = processorRequest;
            this.newPlugin = newPlugin;
        }
    }

    public class RP : IProcessor
    {
        public Action<ProcessorRequest> OnProcessSelected { get; set; }

        private string _pluginAddress = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        private SQL _sql;
        private IPlugin _processor;
        private Associationer _associationer;

        public RP()
        {
            _sql = new SQL(_pluginAddress);
            _associationer = new Associationer(_sql);
            _processor = _associationer;
        }

        public bool GetWorkStatus()
        {
            return !(_processor is Associationer);
        }

        public void Reset()
        {
            _processor.Stop();
            _processor = _associationer;
        }

        public string[] InputChangeProcess(string input)
        {
            if (input.Length == 0) { return Array.Empty<string>(); }
            string[] WordList = this._processor.InputChangeProcess(input);
            if (WordList.Length == 1 && WordList[0] == input) return Array.Empty<string>();
            return WordList;
        }

        public ProcessorRequest Selected(string input)
        {
            PluginRequest request = _processor.Selected(input);
            switch (request.nextProcessor)
            {
                case NextProcessor.None:
                    _processor = _associationer;
                    return new ProcessorRequest(ProcessorRequestType.Close, "");
                case NextProcessor.Self:
                    return new ProcessorRequest(ProcessorRequestType.PluginRemain, "");
                case NextProcessor.Next:
                    _processor = _processor is Associationer ? request.newPlugin : _associationer;
                    Task.Run(() =>
                    {
                        _processor.Start((PluginRequest p) =>
                        {
                            ProcessorRequest processorRequest;
                            switch (p.nextProcessor)
                            {
                                case NextProcessor.None:
                                    _processor = _associationer;
                                    processorRequest = new ProcessorRequest(ProcessorRequestType.Close, "");
                                    break;
                                case NextProcessor.Self:
                                    processorRequest = new ProcessorRequest(ProcessorRequestType.PluginRemain, "");
                                    break;
                                case NextProcessor.Next:
                                    _processor = _associationer;
                                    processorRequest = new ProcessorRequest(ProcessorRequestType.PluginOff, "");
                                    break;
                                default:
                                    _processor = _associationer;
                                    throw new InvalidOperationException();
                            }
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                OnProcessSelected?.Invoke(processorRequest);
                            });
                        });
                    });
                    return new ProcessorRequest(ProcessorRequestType.PluginChange, _processor.GetName());
                default:
                    throw new InvalidOperationException();
            }
        }
    }


    public class Associationer : IPlugin
    {
        private bool _nsfwAllowed = false;
        public SQL _sql;

        public Associationer(SQL sql)
        {
            _sql = sql;
        }

        public string GetName()
        {
            return "";
        }

        public void Start(Action<PluginRequest> callback)
        {
            return;
        }

        public void Stop()
        {
            return;
        }

        public string[] InputChangeProcess(string input)
        {
            SQLResult<List<string>> result = this._sql.Query(input, _nsfwAllowed);
            if (result.status != SQLStatus.Success)
            {
                MessageBox.Show($"模糊搜索错误，错误码：{result.status}.");
                return new string[]{ };
            }
            return result.result.ToArray();
        }

        public PluginRequest Selected(string input)
        {
            if (input == "nsfw")
            {
                _nsfwAllowed = !_nsfwAllowed;
                return new PluginRequest(NextProcessor.Self, new ProcessorRequest(ProcessorRequestType.PluginRemain, ""));
            }
            SQLResult<Queue<RawConfigure>> result = _sql.GetCommandInfo(input, _nsfwAllowed);
            if (result.status != SQLStatus.Success)
            {
                MessageBox.Show($"执行错误，错误码：{result.status}.");
                return new PluginRequest(NextProcessor.None, new ProcessorRequest(ProcessorRequestType.Close, ""));
            }
            return new PluginRequest(NextProcessor.Next, new ProcessorRequest(ProcessorRequestType.PluginChange, ""), new Executer(result.result, input));

        }
    }

    public class PluginProcessResult
    {
        public string[] result;
    }

    public class PluginResult
    {
        public bool Continue;
        public bool Success;
        public string Info;
    }

    public class ProcessResult<T>
    {
        public bool Success;
        public T Result;

        public ProcessResult(bool success, T result)
        {
            Success = success;
            Result = result;
        }
    }

    public class Executer : IPlugin
    {
        private Queue<RawConfigure> _rawConfigures;
        private RawConfigure _configure;
        private bool _isContinue = false;
        private bool _isCancel = false;

        private string Name;

        public Executer(Queue<RawConfigure> c, string name)
        {
            _rawConfigures = c;
            this.Name = name;
        }


        private PluginRequest ExecuteConfig()
        {
            try 
            {
                PluginResult result;

                while (_rawConfigures.Count > 0 || _isContinue)
                {
                    if  (_isCancel)
                    {
                        return new PluginRequest(NextProcessor.Next, new ProcessorRequest(ProcessorRequestType.PluginOff, ""));
                    }

                    if (!_isContinue)
                        _configure = _rawConfigures.Dequeue();
                    if (_configure.Waitable)
                    {
                        _isContinue = true;
                        return new PluginRequest(NextProcessor.Self,
                            new ProcessorRequest(ProcessorRequestType.PluginRemain, ""));
                    }

                    ProcessResult<PluginResult> r = Processer<PluginResult>(_configure.PresetInput);
                    if (!_configure.Associatable)
                    {
                        if (!r.Success)
                            return new PluginRequest(NextProcessor.None,
                                new ProcessorRequest(ProcessorRequestType.Close, ""));
                        _isContinue = false;
                        continue;
                    }
                    if (!r.Success && r.Result.Success)
                    {
                        return new PluginRequest(NextProcessor.None,
                            new ProcessorRequest(ProcessorRequestType.Close, ""));
                    }
                    result = r.Result;
                    if (result.Continue)
                    {
                        _isContinue = true;
                        _configure.Waitable = true;
                    }else
                        _isContinue= false;
                }

                return new PluginRequest(NextProcessor.None,
                    new ProcessorRequest(ProcessorRequestType.Close, ""));
            }
            catch
            {
                return new PluginRequest(NextProcessor.None,
                    new ProcessorRequest(ProcessorRequestType.Close, ""));
            }
        }

        public string[] InputChangeProcess(string input)
        {
            if (!_configure.Associatable) return Array.Empty<string>();

            ProcessResult<PluginProcessResult> r = Processer<PluginProcessResult>("-I " + input);
            try {
                if (!r.Success) { return Array.Empty<string>(); }
                return r.Result.result;
            }
            catch { return Array.Empty<string>(); }
            
        }

        public PluginRequest Selected(string input)
        {
            _configure.PresetInput = input;
            _configure.Waitable = false;
            return ExecuteConfig();
        }

        public void Start(Action<PluginRequest> callback)
        {
            callback?.Invoke(ExecuteConfig());
        }

        public void Stop()
        {
            _isCancel = true;
        }

        public string GetName()
        {
            return Name;
        }

        private ProcessResult<T> Processer<T>(string input)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            if (_configure.Executable)
            {
                if (string.IsNullOrEmpty(_configure.ExecutePath))
                {
                    startInfo.FileName = _configure.SourcePath;
                }
                else
                {
                    startInfo.FileName = _configure.ExecutePath;
                    startInfo.Arguments = _configure.SourcePath;
                }
            }
            else 
            {
                MessageBox.Show("配置不可执行");
                return new ProcessResult<T>(false, default); 
            }

            if (!string.IsNullOrEmpty(input))
            {
                string argument = _configure.Associatable ? (typeof(T) == typeof(PluginProcessResult) ?
                    "-I " + input :
                    "-S " + input) : input;
                startInfo.Arguments += " " + argument;
            }

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = _configure.Associatable;
            startInfo.CreateNoWindow = true;

            using (Process process = Process.Start(startInfo))
            {
                if (!_configure.Associatable) return new ProcessResult<T>(true, default);
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    MessageBox.Show($"进程执行失败，退出码: {process.ExitCode}");
                    return new ProcessResult<T>(false, default);
                }
                return new ProcessResult<T>(true ,JsonSerializer.Deserialize<T>(output));
            }
        }
    }
}





