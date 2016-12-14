﻿using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using NBitcoin;
using System.Diagnostics;
using NBitcoin.RPC;
using NTumbleBit.ClassicTumbler;
using NTumbleBit.TumblerServer.Services;
using NTumbleBit.TumblerServer.Services.RPCServices;
using Microsoft.AspNetCore.Mvc;
using NTumbleBit.Common;

namespace NTumbleBit.TumblerServer
{
	public class ActionResultException : Exception
	{
		public ActionResultException(IActionResult result)
		{
			if(result == null)
				throw new ArgumentNullException("result");
			_Result = result;
		}

		private readonly IActionResult _Result;
		public IActionResult Result
		{
			get
			{
				return _Result;
			}
		}
	}
	public static class Extensions
	{
		public static ActionResultException AsException(this IActionResult actionResult)
		{
			return new ActionResultException(actionResult);
		}
		public static IWebHostBuilder UseAppConfiguration(this IWebHostBuilder builder, TumblerConfiguration configuration)
		{
			builder.ConfigureServices(services =>
			{
				services.AddSingleton(provider =>
				 {
					 var conf = provider.GetRequiredService<TumblerConfiguration>();
					 var repo = provider.GetRequiredService<IRepository>();
					 return new ClassicTumblerRepository(conf, repo);
				 });

				services.AddSingleton<IRepository>(provider =>
				{
					var conf = provider.GetRequiredService<TumblerConfiguration>();
					var dbreeze = new DBreezeRepository(Path.Combine(conf.DataDirectory, "db"));
					return dbreeze;
				});

				services.AddSingleton((provider) =>
				{
					var conf = provider.GetRequiredService<TumblerConfiguration>();
					var repo = provider.GetRequiredService<IRepository>();
					var broadcast = new RPCBroadcastService(conf.RPCClient, repo);
					return new ExternalServices()
					{
						BroadcastService = broadcast,
						FeeService = new RPCFeeService(conf.RPCClient),
						WalletService = new RPCWalletService(conf.RPCClient),
						BlockExplorerService = new RPCBlockExplorerService(conf.RPCClient),
						TrustedBroadcastService = new RPCTrustedBroadcastService(conf.RPCClient, broadcast, repo)
					};
				});
				services.AddSingleton((provider) =>
				{
					var conf = provider.GetRequiredService<TumblerConfiguration>();
					return conf.CreateClassicTumblerParameters();
				});
				services.AddSingleton((provider) =>
				{
					var conf = configuration ?? new TumblerConfiguration();
					var factory = provider.GetRequiredService<ILoggerFactory>();
					var logger = factory.CreateLogger<TumblerConfiguration>();
					conf.Network = conf.Network ?? Network.Main;
					conf.DataDirectory = conf.DataDirectory ?? DefaultDataDirectory.GetDefaultDirectory("NTumbleBitServer", logger, conf.Network);
					conf.ConfigurationFile = conf.ConfigurationFile ?? GetDefaultConfigurationFile(logger, conf.DataDirectory, conf.Network);

					var rsaFile = Path.Combine(conf.DataDirectory, "Tumbler.pem");

					if(conf.TumblerKey == null)
					{
						if(!File.Exists(rsaFile))
						{
							logger.LogWarning("RSA private key not found, please backup it. Creating...");
							conf.TumblerKey = new RsaKey();
							File.WriteAllBytes(rsaFile, conf.TumblerKey.ToBytes());
							logger.LogInformation("RSA key saved (" + rsaFile + ")");
						}
						else
						{
							logger.LogInformation("RSA private key found (" + rsaFile + ")");
							conf.TumblerKey = new RsaKey(File.ReadAllBytes(rsaFile));
						}
					}

					if(conf.VoucherKey == null)
					{
						var voucherFile = Path.Combine(conf.DataDirectory, "Voucher.pem");
						if(!File.Exists(voucherFile))
						{
							logger.LogWarning("Creation of Voucher Key");
							conf.VoucherKey = new RsaKey();
							File.WriteAllBytes(voucherFile, conf.VoucherKey.ToBytes());
							logger.LogInformation("RSA key saved (" + voucherFile + ")");
						}
						else
						{
							logger.LogInformation("Voucher key found (" + voucherFile + ")");
							conf.VoucherKey = new RsaKey(File.ReadAllBytes(voucherFile));
						}
					}

					Debug.Assert(conf.TumblerKey != null);
					Debug.Assert(conf.VoucherKey != null);


					conf.RPCClient = conf.RPCClient ?? RPCConfiguration.ConfigureRPCClient(logger, conf.ConfigurationFile, conf.Network);
					return configuration;
				});
			});
			return builder;
		}		

		public static string GetDefaultConfigurationFile(ILogger logger, string dataDirectory, Network network)
		{
			var config = Path.Combine(dataDirectory, "server.config");
			logger.LogInformation("Configuration file set to " + config);
			if(!File.Exists(config))
			{
				logger.LogInformation("Creating configuration file");

				var data = TextFileConfiguration.CreateDefaultConfiguration(network);
				File.WriteAllText(config, data);
			}
			return config;
		}
	}
}
