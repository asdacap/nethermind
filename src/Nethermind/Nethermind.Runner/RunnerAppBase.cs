﻿/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;
using Nethermind.Config;
using Nethermind.Core;
using Nethermind.Core.Logging;
using Nethermind.Core.Specs.ChainSpec;
using Nethermind.Core.Utils;
using Nethermind.JsonRpc.Config;
using Nethermind.KeyStore.Config;
using Nethermind.Network;
using Nethermind.Network.Config;
using Nethermind.Runner.Config;
using Nethermind.Runner.Runners;

namespace Nethermind.Runner
{
    public abstract class RunnerAppBase
    {
        protected ILogger Logger;
        protected readonly IPrivateKeyProvider PrivateKeyProvider;
        private IJsonRpcRunner _jsonRpcRunner = NullRunner.Instance;
        private IEthereumRunner _ethereumRunner = NullRunner.Instance;

        protected RunnerAppBase(ILogger logger, IPrivateKeyProvider privateKeyProvider)
        {
            Logger = logger;
            PrivateKeyProvider = privateKeyProvider;
        }

        public void Run(string[] args)
        {
            var (app, buildConfigProvider, getDbBasePath) = BuildCommandLineApp();
            ManualResetEvent appClosed = new ManualResetEvent(false);
            app.OnExecute(async () =>
            {
                var configProvider = buildConfigProvider();
                var initConfig = configProvider.GetConfig<IInitConfig>();
                
                if (initConfig.RemovingLogFilesEnabled)
                {
                    RemoveLogFiles(initConfig.LogDirectory);
                }

                Logger = new NLogLogger(initConfig.LogFileName, initConfig.LogDirectory);

                var pathDbPath = getDbBasePath();
                if (!string.IsNullOrWhiteSpace(pathDbPath))
                {
                    var newDbPath = Path.Combine(pathDbPath, initConfig.BaseDbPath);
                    Logger.Info($"Adding prefix to baseDbPath, new value: {newDbPath}, old value: {initConfig.BaseDbPath}");
                    initConfig.BaseDbPath = newDbPath;
                }

                Console.Title = initConfig.LogFileName;

                var serializer = new UnforgivingJsonSerializer();
                Logger.Info($"Running Nethermind Runner, parameters:\n{serializer.Serialize(initConfig, true)}\n");

                Task userCancelTask = Task.Factory.StartNew(() =>
                {
                    Console.WriteLine("Enter 'e' to exit");
                    while (true)
                    {
                        ConsoleKeyInfo keyInfo = Console.ReadKey();
                        if (keyInfo.KeyChar == 'e')
                        {
                            break;
                        }
                    }
                });

                await StartRunners(configProvider);
                await userCancelTask;

                Console.WriteLine("Closing, please wait until all functions are stopped properly...");
                StopAsync().Wait();
                Console.WriteLine("All done, goodbye!");
                appClosed.Set();

                return 0;
            });

            app.Execute(args);
            appClosed.WaitOne();
        }

        protected async Task StartRunners(IConfigProvider configProvider)
        {
            try
            {
                var initParams = configProvider.GetConfig<IInitConfig>();
                var logManager = new NLogManager(initParams.LogFileName, initParams.LogDirectory);

                //discovering and setting local, remote ips for client machine
                var networkHelper = new NetworkHelper(Logger);
                var localHost = networkHelper.GetLocalIp()?.ToString() ?? "127.0.0.1";
                var networkConfig = configProvider.GetConfig<INetworkConfig>();
                networkConfig.MasterExternalIp = localHost;
                networkConfig.MasterHost = localHost;
                
                ChainSpecLoader chainSpecLoader = new ChainSpecLoader(new UnforgivingJsonSerializer());

                string path = initParams.ChainSpecPath;
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));
                }

                byte[] chainSpecData = File.ReadAllBytes(path);
                ChainSpec chainSpec = chainSpecLoader.Load(chainSpecData);

                var nodes = chainSpec.NetworkNodes.Select(nn => GetNode(nn, localHost)).ToArray();
                networkConfig.BootNodes = nodes;
                networkConfig.DbBasePath = initParams.BaseDbPath;

                _ethereumRunner = new EthereumRunner(configProvider, networkHelper, logManager);
                 await _ethereumRunner.Start().ContinueWith(x =>
                 {
                     if (x.IsFaulted && Logger.IsError) Logger.Error("Error during ethereum runner start", x.Exception);
                 });

                if (initParams.JsonRpcEnabled)
                {
                    Bootstrap.Instance.ConfigProvider = configProvider;
                    Bootstrap.Instance.LogManager = logManager;
                    Bootstrap.Instance.BlockchainBridge = _ethereumRunner.BlockchainBridge;
                    Bootstrap.Instance.EthereumSigner = _ethereumRunner.EthereumSigner;

                    _jsonRpcRunner = new JsonRpcRunner(configProvider, logManager);
                    await _jsonRpcRunner.Start().ContinueWith(x =>
                    {
                        if (x.IsFaulted && Logger.IsError) Logger.Error("Error during jsonRpc runner start", x.Exception);
                    });
                }
                else
                {
                    if (Logger.IsInfo)
                    {
                        Logger.Info("Json RPC is disabled");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("Error while starting Nethermind.Runner", e);
                throw;
            }
        }

        protected abstract (CommandLineApplication, Func<IConfigProvider>, Func<string>) BuildCommandLineApp();

        protected async Task StopAsync()
        {
            if (_jsonRpcRunner != null)
            {
                await _jsonRpcRunner.StopAsync();
            }

            if (_ethereumRunner != null)
            {
                await _ethereumRunner.StopAsync();
            }
        }

        private ConfigNode GetNode(NetworkNode networkNode, string localHost)
        {
            var node = new ConfigNode
            {
                NodeId = networkNode.NodeId.PublicKey.ToString(false),
                Host = networkNode.Host == "127.0.0.1" ? localHost : networkNode.Host,
                Port = networkNode.Port,
                Description = networkNode.Description
            };
            return node;
        }

        private void RemoveLogFiles(string logDirectory)
        {
            Console.WriteLine("Removing log files.");

            var logsDir = string.IsNullOrEmpty(logDirectory) ?  Path.Combine(PathUtils.GetExecutingDirectory(), "logs") : logDirectory;
            if (!Directory.Exists(logsDir))
            {
                return;
            }

            var files = Directory.GetFiles(logsDir);
            foreach (string file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error removing log file: {file}, exp: {e}");
                }
            }
        }
    }
}