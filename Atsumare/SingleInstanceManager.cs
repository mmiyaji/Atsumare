using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atsumare
{
    internal sealed class SingleInstanceManager : IDisposable
    {
        private readonly string _mutexName;
        private readonly string _signalName;

        private Mutex? _mutex;
        private EventWaitHandle? _signal;
        private bool _ownsMutex;

        public SingleInstanceManager(string mutexName, string signalName)
        {
            _mutexName = mutexName;
            _signalName = signalName;
        }

        public bool TryEnterAsFirstInstance()
        {
            _mutex = new Mutex(initiallyOwned: true, name: _mutexName, createdNew: out bool createdNew);
            _ownsMutex = createdNew;

            // 既存プロセスに通知するためのシグナル
            _signal = new EventWaitHandle(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: _signalName
            );

            return _ownsMutex;
        }

        public void SignalShowRequest()
        {
            try
            {
                using var ewh = EventWaitHandle.OpenExisting(_signalName);
                ewh.Set();
            }
            catch
            {
                // 既存がいない/権限等で開けない場合は無視
            }
        }

        public Task WaitForShowRequestAsync()
        {
            if (_signal == null) throw new InvalidOperationException("Signal handle not initialized.");

            return Task.Run(() =>
            {
                _signal.WaitOne();
            });
        }

        public void Dispose()
        {
            try
            {
                if (_ownsMutex) _mutex?.ReleaseMutex();
            }
            catch { }

            _signal?.Dispose();
            _mutex?.Dispose();
        }
    }
}