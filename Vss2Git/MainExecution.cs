using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Hpdi.VssLogicalLib;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Singleton execution manager
    /// </summary>
    /// <author>Brad Williams</author>
    public sealed class MainExecution
    {
        private static MainExecution instance = null;
        private readonly WorkQueue workQueue = new WorkQueue(1);
        private Logger logger = Logger.Null;
        private RevisionAnalyzer revisionAnalyzer;
        private ChangesetBuilder changesetBuilder;
        public Settings Settings { get; set; } = new Settings();
        private static readonly object padlock = new object();

        private MainExecution()
        {
        }

        public static MainExecution Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (padlock)
                    {
                        if (instance == null)
                        {
                            instance = new MainExecution();
                        }
                    }
                }
                return instance;
            }
        }

        public List<Tuple<string, string>> ImportSettings(string filePath)
        {
            return Settings.ParseSettingsFile(filePath);
        }

        private void OpenLog(string filename)
        {
            logger = string.IsNullOrEmpty(filename) ? Logger.Null : new Logger(filename);
        }

        public void StartConversion()
        {
            try
            {
                OpenLog(Settings.LogFile);

                logger.WriteLine("VSS2Git version {0}", Assembly.GetExecutingAssembly().GetName().Version);

                Encoding encoding = Encoding.GetEncoding(Settings.Encoding);

                logger.WriteLine("VSS encoding: {0} (CP: {1}, IANA: {2})",
                    encoding.EncodingName, encoding.CodePage, encoding.WebName);
                logger.WriteLine("Comment transcoding: {0}",
                    Settings.TranscodeComments ? "enabled" : "disabled");
                logger.WriteLine("Ignore errors: {0}",
                    Settings.IgnoreGitErrors ? "enabled" : "disabled");

                var df = new VssDatabaseFactory(Settings.VssDirectory);
                df.Encoding = encoding;
                var db = df.Open();

                var path = Settings.VssProject;
                VssItem item;
                try
                {
                    item = db.GetItem(path);
                }
                catch (VssPathException ex)
                {
                    throw ex;
                }

                var project = item as VssProject;
                if (project == null)
                {
                    throw new VssPathException(string.Format("{0} is not a project", path));
                }

                revisionAnalyzer = new RevisionAnalyzer(workQueue, logger, db);
                if (!string.IsNullOrEmpty(Settings.VssExcludePaths))
                {
                    revisionAnalyzer.ExcludeFiles = Settings.VssExcludePaths;
                }
                revisionAnalyzer.AddItem(project);

                changesetBuilder = new ChangesetBuilder(workQueue, logger, revisionAnalyzer);
                changesetBuilder.AnyCommentThreshold = TimeSpan.FromSeconds((double)Settings.AnyCommentSeconds);
                changesetBuilder.SameCommentThreshold = TimeSpan.FromSeconds((double)Settings.SameCommentSeconds);
                changesetBuilder.BuildChangesets();

                if (!string.IsNullOrEmpty(Settings.GitDirectory))
                {
                    var gitExporter = new GitExporter(workQueue, logger,
                        revisionAnalyzer, changesetBuilder);
                    if (!string.IsNullOrEmpty(Settings.DefaultEmailDomain))
                    {
                        gitExporter.EmailDomain = Settings.DefaultEmailDomain;
                    }
                    if (!string.IsNullOrEmpty(Settings.DefaultComment))
                    {
                        gitExporter.DefaultComment = Settings.DefaultComment;
                    }
                    if (!Settings.TranscodeComments)
                    {
                        gitExporter.CommitEncoding = encoding;
                    }
                    gitExporter.IgnoreErrors = Settings.IgnoreGitErrors;
                    gitExporter.ExportToGit(Settings.GitDirectory);
                }

                workQueue.Idle += delegate
                {
                    logger.Dispose();
                    logger = Logger.Null;
                };

                workQueue.ThrowException += logException;
            }
            catch (Exception ex)
            {
                logger.Dispose();
                logger = Logger.Null;
                throw ex;
            }
        }

        private void logException(object sender, ExpThrownEventArgs e)
        {
            var message = ExceptionFormatter.Format(e.Exception);

            logger.WriteLine("ERROR: {0}", message);
            logger.WriteLine(e.Exception);
        }

        public void workQueueAbort()
        {
            workQueue.Abort();
        }

        public void workQueueWaitIdle()
        {
            workQueue.WaitIdle();
        }

        public string getWorkQueueLastStatus()
        {
            return workQueue.LastStatus;
        }
        public DateTime getElapsedTime()
        {
            return new DateTime(workQueue.ActiveTime.Ticks);
        }
        public ICollection<Exception> getWorkQueueExceptions()
        {
            return workQueue.FetchExceptions();
        }

        public bool isWorkQueueIdle()
        {
            if (workQueue != null)
            {
                return workQueue.IsIdle;
            }

            return false;
        }

        public int getRevAnalyzerFileCount()
        {
            if (revisionAnalyzer != null)
            {
                return revisionAnalyzer.FileCount;
            }

            return 0;
        }

        public int getRevAnalyzerRevCount()
        {
            if (revisionAnalyzer != null)
            {
                return revisionAnalyzer.RevisionCount;
            }

            return 0;
        }

        public int getChangesetCount()
        {
            if (changesetBuilder != null)
            {
                return changesetBuilder.Changesets.Count;
            }

            return 0;
        }

        public void nullifyObjs()
        {
            revisionAnalyzer = null;
            changesetBuilder = null;
        }
    }

}
