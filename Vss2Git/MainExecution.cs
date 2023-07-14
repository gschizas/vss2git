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
                if (instance != null) return instance;
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new MainExecution();
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

                var encoding = Encoding.GetEncoding(Settings.Encoding);

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
                var item = db.GetItem(path);

                var project = item as VssProject ?? throw new VssPathException($"{path} is not a project");
                revisionAnalyzer = new RevisionAnalyzer(workQueue, logger, db);
                if (!string.IsNullOrEmpty(Settings.VssExcludePaths))
                {
                    revisionAnalyzer.ExcludeFiles = Settings.VssExcludePaths;
                }
                revisionAnalyzer.AddItem(project);

                changesetBuilder = new ChangesetBuilder(workQueue, logger, revisionAnalyzer)
                {
                    AnyCommentThreshold = TimeSpan.FromSeconds(Settings.AnyCommentSeconds),
                    SameCommentThreshold = TimeSpan.FromSeconds(Settings.SameCommentSeconds)
                };
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
                throw;
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
        public TimeSpan getElapsedTime()
        {
            return new TimeSpan(workQueue.ActiveTime.Ticks);
        }
        public ICollection<Exception> getWorkQueueExceptions()
        {
            return workQueue.FetchExceptions();
        }

        public bool isWorkQueueIdle()
        {
            return workQueue != null && workQueue.IsIdle;
        }

        public int getRevAnalyzerFileCount()
        {
            return revisionAnalyzer?.FileCount ?? 0;
        }

        public int getRevAnalyzerRevCount()
        {
            return revisionAnalyzer?.RevisionCount ?? 0;
        }

        public int getChangesetCount()
        {
            return changesetBuilder == null ? 0 : changesetBuilder.Changesets.Count;
        }

        public int getChangesetId()
        {
            return workQueue.ChangesetId;
        }

        public void nullifyObjs()
        {
            revisionAnalyzer = null;
            changesetBuilder = null;
        }
    }

}
