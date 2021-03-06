using System;
using System.Text;
using Bam.Net;
using Bam.Net.Automation;
using Bam.Net.CommandLine;
using Bam.Net.Data.Repositories;

namespace Bam.Shell.Jobs
{
    public class JobProvider : ShellProvider
    {
        JobManagerService _jobManagerService;
        public JobManagerService JobManagerService
        {
            get
            {
                if (_jobManagerService == null)
                {
                    _jobManagerService = JobManagerService.Current;
                }
                return _jobManagerService; 
                
            }
            set => _jobManagerService = value;
        }

        public override void RegisterArguments(string[] args)
        {
            RawArguments = args;
            base.RegisterArguments(args);
            AddValidArgument("copyName", "The name of the job copy");
            AddValidArgument("newName", "The name to rename the job to");
        }
        
        protected override ProviderArguments GetProviderArguments()
        {
            return GetProviderArguments(false);
        }
        
        public new string[] RawArguments { get; private set; }
        
        protected ProviderArguments GetProviderArguments(bool full = false)
        {
            ProviderArguments baseArgs = base.GetProviderArguments();
            JobProviderArguments providerArguments = baseArgs.CopyAs<JobProviderArguments>();
            providerArguments.JobName = providerArguments.ProviderContextTarget;
            return providerArguments;
        }

        public override void List(Action<string> output = null, Action<string> error = null)
        {
            try
            {
                PrintMessage();
                StringBuilder jobs = new StringBuilder();
                foreach (string jobName in JobManagerService.ListJobNames())
                {
                    jobs.AppendLine(jobName);
                }
                OutLine(jobs.ToString(), ConsoleColor.Blue);
            }
            catch (Exception ex)
            {
                Message.PrintLine("Exception listing jobs: {0}", ex.Message);
                Exit(1);
            }

            Exit(0);
        }

        public override void Add(Action<string> output = null, Action<string> error = null)
        {
            try
            {
                PrintMessage();
                JobProviderArguments arguments = GetProviderArguments() as JobProviderArguments;
                string jobName = arguments.JobName;
                JobConf jobConf = JobManagerService.GetJob(jobName);
                
                string[] workerTypes = JobManagerService.GetWorkerTypes();
                int workerIndex = SelectFrom(workerTypes);
                string workerName = GetArgument("workerName", "Please enter a name for the worker");
                JobManagerService.AddWorker(jobName, workerTypes[workerIndex], workerName);
                JobManagerService.SaveJob(jobConf);
                OutLine(jobConf.ToYaml(), ConsoleColor.Cyan);
                Message.PrintLine("Created job {0} in directory {1}", ConsoleColor.Cyan, jobConf.Name, jobConf.JobDirectory);
            }
            catch (Exception ex)
            {
                Message.PrintLine("Exception adding job: {0}", ex.Message);
                Exit(1);
            }
            Exit(0);
        }

        public override void Copy(Action<string> output = null, Action<string> error = null)
        {
            try
            {
                PrintMessage();
                JobProviderArguments arguments = GetProviderArguments() as JobProviderArguments;
                string jobName = arguments.JobName;
                
                JobConf copy = JobManagerService.CopyJob(jobName,
                    GetArgument("copyName", "Please enter the name of the copy"));
                Message.PrintLine(copy.ToYaml(), ConsoleColor.Cyan);
                Message.PrintLine("Copied job {0} to {1} in directory {2}", ConsoleColor.Cyan, jobName, copy.Name, copy.JobDirectory);
            }
            catch (Exception ex)
            {
                Message.PrintLine("Exception copying job: {0}", ex.Message);
                Exit(1);
            }
            Exit(0);
        }

        public override void Rename(Action<string> output = null, Action<string> error = null)
        {
            try
            {
                PrintMessage();
                JobProviderArguments arguments = GetProviderArguments() as JobProviderArguments;
                string jobName = arguments.JobName;

                JobConf jobConf = JobManagerService.RenameJob(jobName,
                    GetArgument("newName", "Please enter the new name to give to the job"));

                Message.PrintLine(jobConf.ToYaml(), ConsoleColor.Cyan);
                Message.PrintLine("Renamed job {0} to {1} in directory {2}", ConsoleColor.Cyan, jobName, jobConf.Name, jobConf.JobDirectory);
            }
            catch (Exception ex)
            {
                Message.PrintLine("Exception renaming job: {0}", ex.Message);
                Exit(1);
            }
            Exit(0);
        }
        
        public override void Show(Action<string> output = null, Action<string> error = null)
        {
            try
            {
                PrintMessage();
                JobProviderArguments arguments = GetProviderArguments() as JobProviderArguments;
                string jobName = arguments.JobName;

                if (JobManagerService.JobExists(jobName))
                {
                    JobConf jobConf = JobManagerService.GetJob(jobName);
                    OutLine(jobConf.ToYaml(), ConsoleColor.Cyan);
                }
                else
                {
                    PrintMessage();
                    Message.PrintLine("Specified job does not exist: {0}", ConsoleColor.Yellow, jobName);
                }
            }
            catch (Exception ex)
            {
                Message.PrintLine("Exception getting job: {0}", ex.Message);
                Exit(1);
            }

            Exit(0);
        }

        public override void Remove(Action<string> output = null, Action<string> error = null)
        {
            try
            {
                JobProviderArguments providerArguments = GetProviderArguments() as JobProviderArguments;
                if(JobManagerService.JobExists(providerArguments.JobName))
                {
                    JobManagerService.RemoveJob(providerArguments.JobName);
                    Message.PrintLine("Job {0} was deleted", ConsoleColor.Yellow, providerArguments.JobName);
                }
                else
                {
                    PrintMessage();
                    Message.PrintLine("Specified job doesn't exist: {0}", providerArguments.JobName);
                }
            }
            catch (Exception ex)
            {
                Message.PrintLine("Exception removing job: {0}", ConsoleColor.Magenta, ex.Message);
                Exit(1);
            }

            Exit(0);
        }

        public override void Run(Action<string> output = null, Action<string> error = null)
        {
            try
            {
                PrintMessage();
                JobProviderArguments providerArguments = GetProviderArguments() as JobProviderArguments;
                if (JobManagerService.JobExists(providerArguments.JobName))
                {
                    bool? jobFinished = false;
                    JobManagerService.JobFinished += (sender, args) =>
                    {
                        jobFinished = (args.Cast<WorkStateEventArgs>())?.WorkState?.JobName?.Equals(providerArguments.JobName);
                        if (jobFinished != null && jobFinished.Value == true)
                        {
                            Message.PrintLine("Job {0} finished", ConsoleColor.Cyan, providerArguments.JobName);
                            Unblock();
                        }
                    };
                    JobManagerService.StartJob(providerArguments.JobName);
                    Message.PrintLine("Job {0} was queued to start", ConsoleColor.DarkGreen, providerArguments.JobName);
                    Block();
                }
                else
                {
                    Message.PrintLine("Specified job {0} does not exist", ConsoleColor.Magenta, providerArguments.JobName);
                }
            }
            catch (Exception ex)
            {
                Message.PrintLine("Exception running job: {0}", ConsoleColor.Magenta, ex.Message);
                Exit(1);
            }
            Exit(0);
        }

        private void PrintMessage()
        {
            Message.PrintLine("Jobs directory: {0}", ConsoleColor.Yellow, JobManagerService.JobsDirectory);
        }
    }
}