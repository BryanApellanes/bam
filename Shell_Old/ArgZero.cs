using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bam.Net;
using Bam.Net.CommandLine;
using Bam.Net.Logging;
using Bam.Net.Testing;

namespace Bam.Shell
{
    /// <summary>
    /// A class representing the first argument to the entry assembly.  Exists primarily to allow
    /// processing of arguments without a prefix and to route execution to a method specified on
    /// the command line.
    /// </summary>
    public class ArgZero : CommandLineTool
    {
        static readonly object _targetsLock = new object();
        static Dictionary<string, MethodInfo> _targets; 
        public static Dictionary<string, MethodInfo> Targets
        {
            get
            {
                return _targetsLock.DoubleCheckLock(ref _targets,
                    () => new Dictionary<string, MethodInfo>());
            }
        }

        public static void Register(string arg, MethodInfo method)
        {
            if (!Targets.AddMissing(arg, method))
            {
                MethodInfo registered = Targets[arg];
                string willUse = $"{registered.DeclaringType.Name}.{registered.Name}";
                Message.PrintLine("The specified ArgZero is already registered {0}, will use {1}", ConsoleColor.Yellow, arg, willUse);
            }
        }

        /// <summary>
        /// Register extenders of type T as ArgZero providers
        /// </summary>
        public static void RegisterArgZeroProviders<T>(string[] args) where T: IRegisterArguments
        {
            RegisterArgZeroProviders<T>(args, typeof(T).Assembly);
        }
        
        /// <summary>
        /// Register extenders of type T as ArgZero providers
        /// </summary>
        public static void RegisterArgZeroProviders<T>(string[] args, Assembly assembly) where T: IRegisterArguments
        {
            RegisterArgZeroProviderTypeArguments<T>(args, assembly);
            RegisterDelegatorMethods(assembly);
        }

        static readonly HashSet<Assembly> _regsiteredDelegatorAssemblies = new HashSet<Assembly>();
        static readonly object _registerLock = new object();
        public static void RegisterDelegatorMethods(Assembly assembly)
        {
            lock (_registerLock)
            {
                if (_regsiteredDelegatorAssemblies.Contains(assembly))
                {
                    return;
                }

                _regsiteredDelegatorAssemblies.Add(assembly);
                foreach (Type type in assembly.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods())
                    {
                        if (method.HasCustomAttributeOfType<ArgZeroAttribute>(out ArgZeroAttribute arg))
                        {
                            Register(arg.Argument, method);
                        }
                    }
                }
            }
        }
        
        public static void RegisterArgZeroProviderTypes(Type type, string[] args, Assembly assembly)
        {
            foreach (Type extender in assembly.GetTypes().Where(t => t.ExtendsType(type)))
            {
                ((IRegisterArguments)extender.Construct()).RegisterArguments(args);
                string name = extender.Name;
                if (!extender.Name.EndsWith("Provider"))
                {
                    Message.PrintLine("For clarity and convention, the name of type {0} should end with 'Provider'", ConsoleColor.Yellow);
                }
                else
                {
                    name = name.Truncate("Provider".Length);
                }

                ProviderTypes.AddMissing(name, extender);
            }
        }
        
        /// <summary>
        /// Register extenders of the specified type from the specified assembly as shell providers ensuring that their required arguments
        /// are also properly registered.
        /// </summary>
        /// <param name="args"></param>
        /// <param name="assembly"></param>
        /// <typeparam name="T"></typeparam>
        public static void RegisterArgZeroProviderTypeArguments<T>(string[] args, Assembly assembly) where T : IRegisterArguments
        {
            foreach (Type type in assembly.GetTypes().Where(type => type.ExtendsType<T>()))
            {
                type.Construct<T>().RegisterArguments(args);
                string name = type.Name;
                if (!type.Name.EndsWith("Provider"))
                {
                    Message.PrintLine("For clarity and convention, the name of type {0} should end with 'Provider'", ConsoleColor.Yellow);
                }
                else
                {
                    name = name.Truncate("Provider".Length);
                }

                ProviderTypes.AddMissing(name, type);
            }
        }
        
        private static Dictionary<string, Type> _providerTypes;
        static readonly object _providerTypesLock = new object();
        public static Dictionary<string, Type> ProviderTypes
        {
            get { return _providerTypesLock.DoubleCheckLock(ref _providerTypes, () => new Dictionary<string, Type>()); }
        }

        public static void ExecuteArgZero(string[] arguments)
        {
            ExecuteArgZero(arguments, () => Exit(0));
        }
        
        /// <summary>
        /// Execute any ArgZero arguments specified on the command line.  Has no effect if no relevant arguments
        /// are detected.
        /// </summary>
        public static void ExecuteArgZero(string[] arguments, Action onArgZeroExecuted = null)
        {
            if (arguments.Length == 0)
            {
                return;
            }

            if (Environment.GetCommandLineArgs().Any(a => a.Equals("/pause")))
            {
                Pause("Press enter to continue");
            }

            onArgZeroExecuted ??= (() => { });
            
            if (Targets.ContainsKey(arguments[0]))
            {
                List<string> targetArguments = new List<string>();
                List<ArgumentInfo> argumentInfos = new List<ArgumentInfo>();
                arguments.Rest(2, (val) =>
                {
                    targetArguments.Add(val);
                    if (val.StartsWith("/"))
                    {
                        string argName = val.TruncateFront(1).ReadUntil(':', out string argVal);
                        argumentInfos.Add(new ArgumentInfo(argName, true));
                    }
                });
                Arguments = new ParsedArguments(targetArguments.ToArray(), argumentInfos.ToArray());
                MethodInfo method = Targets[arguments[0]];
                if (method.HasCustomAttributeOfType<ArgZeroAttribute>(out ArgZeroAttribute argZeroAttribute))
                {
                    if (argZeroAttribute.BaseType != null)
                    {
                        RegisterArgZeroProviderTypes(argZeroAttribute.BaseType, arguments, argZeroAttribute.BaseType.Assembly);
                    }
                }
                
                object instance = null;
                if (!method.IsStatic)
                {
                    instance = method.DeclaringType.Construct();
                }

                try
                {
                    ArgZeroDelegator.CommandLineArguments = arguments;
                    method.Invoke(instance, null);
                }
                catch (Exception ex)
                {
                    Message.PrintLine("Exception executing ArgZero: {0}", ConsoleColor.Magenta, ex.Message);
                }

                onArgZeroExecuted();
            }
        }
        
        private static IEnumerable<Assembly> FindAssemblies()
        {
            Config config = Config.Current;
            string arg0DirPath = Path.Combine(Workspace.Current.Directory("arg0").FullName);
            string argZeroAssemblyFolders = config.AppSettings["ArgZeroAssemblyFolders"].Or($".,{arg0DirPath}");
            string argZeroScanPattern = config.AppSettings["ArgZeroScanPattern"].Or("*-arg0.dll");

            string[] assemblyFolderPaths = argZeroAssemblyFolders.DelimitSplit(",");
            foreach (string assemblyFolderPath in assemblyFolderPaths)
            {
                foreach (Assembly assembly in FindAssemblies(new DirectoryInfo(assemblyFolderPath), argZeroScanPattern))
                {
                    yield return assembly;
                }
            }
        }
        
        private static IEnumerable<Assembly> FindAssemblies(DirectoryInfo directoryInfo, string searchPattern)
        {
            FileInfo[] fileInfos = directoryInfo.GetFiles(searchPattern);
            foreach (FileInfo fileInfo in fileInfos)
            {
                Assembly next = null;
                try
                {
                    next = Assembly.LoadFile(fileInfo.FullName);
                }
                catch (Exception ex)
                {
                    Log.Warn("Error finding assemblies in directory {0}: {1}", directoryInfo.FullName, ex.Message);
                    continue;
                }

                if (next != null)
                {
                    yield return next;   
                }
            }
        }
    }
}