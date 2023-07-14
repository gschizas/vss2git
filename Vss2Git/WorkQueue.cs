/* Copyright 2009 HPDI, LLC
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Extends the simple work queue with support for tracking worker status and exceptions.
    /// </summary>
    /// <author>Trevor Robinson</author>
    public class WorkQueue : SimpleWorkQueue
    {
        private readonly ManualResetEvent idleEvent = new ManualResetEvent(true);
        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly LinkedList<Exception> workExceptions = new LinkedList<Exception>();
        private readonly Dictionary<object, string> workStatuses = new Dictionary<object, string>();
        private object lastStatusWork;

        public string LastStatus { get; private set; }

        public WorkQueue()
        {
        }

        public WorkQueue(int maxThreads)
            : base(maxThreads)
        {
        }

        public TimeSpan ActiveTime => stopwatch.Elapsed;

        public WaitHandle IdleEvent => idleEvent;

        public event EventHandler Idle;
        public event EventHandler<ExpThrownEventArgs> ThrowException;

        public void WaitIdle()
        {
            idleEvent.WaitOne();
        }

        public ICollection<Exception> FetchExceptions()
        {
            lock (workExceptions)
            {
                if (workExceptions.Count <= 0) return null;
                    var result = new List<Exception>(workExceptions);
                    workExceptions.Clear();
                    return result;
                }
            }

        public string GetStatus(object work)
        {
            string result;
            lock (workStatuses)
            {
                workStatuses.TryGetValue(work, out result);
            }
            return result;
        }

        public void SetStatus(object work, string status)
        {
            lock (workStatuses)
            {
                // only allow status to be set if key is already present,
                // so we know that it will be removed in OnStop
                if (!workStatuses.ContainsKey(work)) return;
                    workStatuses[work] = status;
                    if (string.IsNullOrEmpty(status))
                    {
                        WorkStatusCleared(work);
                    }
                    else
                    {
                        lastStatusWork = work;
                    LastStatus = status;
                }
            }
        }

        public void ClearStatus(object work)
        {
            SetStatus(work, null);
        }

        protected override void OnActive()
        {
            base.OnActive();
            idleEvent.Reset();
            stopwatch.Start();
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            stopwatch.Stop();
            idleEvent.Set();

            var handler = Idle;
            handler?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnStart(WaitCallback work)
        {
            base.OnStart(work);
            lock (workStatuses)
            {
                workStatuses[work] = null;
            }
        }

        protected override void OnStop(WaitCallback work)
        {
            base.OnStop(work);
            lock (workStatuses)
            {
                workStatuses.Remove(work);
                WorkStatusCleared(work);
            }
        }

        protected override void OnException(WaitCallback work, Exception e)
        {
            base.OnException(work, e);

            var handler = ThrowException;
            if (handler != null)
            {
                var eventArgs = new ExpThrownEventArgs { Exception = e };
                handler(this, eventArgs);
            }

            lock (workExceptions)
            {
                workExceptions.AddLast(e);
            }
        }

        // Assumes work status lock is held.
        private void WorkStatusCleared(object work)
        {
            if (work != lastStatusWork) return;
                lastStatusWork = null;
            LastStatus = null;

            foreach (var entry in workStatuses.Where(entry => !string.IsNullOrEmpty(entry.Value)))
                    {
                        lastStatusWork = entry.Key;
                LastStatus = entry.Value;
                        break;
                    }
                }
            }

    public class ExpThrownEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
    }
}
