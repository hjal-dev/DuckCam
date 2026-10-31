using System;
using System.Collections.Generic;

namespace DuckCam.Core
{
    internal sealed class RestoreHandle
    {
        private readonly Action _restore;

        internal RestoreHandle(string name, Action restore)
        {
            Name = name;
            _restore = restore;
        }

        public string Name { get; }

        public bool Done { get; private set; }

        public void Dismiss()
        {
            Done = true;
        }

        public string Run()
        {
            if (Done)
            {
                return null;
            }

            Done = true;
            try
            {
                _restore();
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }
    }

    internal sealed class RestoreStack
    {
        private readonly List<RestoreHandle> _entries = new List<RestoreHandle>();

        public int PendingCount
        {
            get
            {
                int n = 0;
                foreach (RestoreHandle e in _entries)
                {
                    if (!e.Done)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        public IEnumerable<string> PendingNames
        {
            get
            {
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (!_entries[i].Done)
                    {
                        yield return _entries[i].Name;
                    }
                }
            }
        }

        public RestoreHandle Push(string name, Action restore)
        {
            var handle = new RestoreHandle(name, restore);
            _entries.Add(handle);
            PhotoLog.Debug("restore registered: " + name);
            return handle;
        }

        public List<string> UnwindAll()
        {
            var failures = new List<string>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                RestoreHandle entry = _entries[i];
                if (entry.Done)
                {
                    continue;
                }

                string error = entry.Run();
                if (error == null)
                {
                    PhotoLog.Verbose("restored: " + entry.Name);
                }
                else
                {
                    failures.Add(entry.Name + ": " + error);
                    PhotoLog.Error($"Restore '{entry.Name}' failed: {error}");
                }
            }

            _entries.Clear();
            return failures;
        }
    }
}
