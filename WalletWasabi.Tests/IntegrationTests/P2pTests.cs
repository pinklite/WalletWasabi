using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models;
using WalletWasabi.CoinJoin.Client.Clients;
using WalletWasabi.Crypto;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;
using WalletWasabi.KeyManagement;
using WalletWasabi.Logging;
using WalletWasabi.Mempool;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.NodeBuilding;
using WalletWasabi.Tests.XunitConfiguration;
using Xunit;

namespace WalletWasabi.Tests.IntegrationTests
{
	public class P2pTests
	{
		[Theory]
		// [InlineData("test")] - ToDo, this test fails for some reason.
		[InlineData("main")]
		public async Task TestServicesAsync(string networkString)
		{
			await RuntimeParams.LoadAsync();
			var network = Network.GetNetwork(networkString);
			var blocksToDownload = new HashSet<uint256>();
			if (network == Network.Main)
			{
				blocksToDownload.Add(new uint256("00000000000000000037c2de35bd85f3e57f14ddd741ce6cee5b28e51473d5d0"));
				blocksToDownload.Add(new uint256("000000000000000000115315a43cb0cdfc4ea54a0e92bed127f4e395e718d8f9"));
				blocksToDownload.Add(new uint256("00000000000000000011b5b042ad0522b69aae36f7de796f563c895714bbd629"));
			}
			else if (network == Network.TestNet)
			{
				blocksToDownload.Add(new uint256("0000000097a664c4084b49faa6fd4417055cb8e5aac480abc31ddc57a8208524"));
				blocksToDownload.Add(new uint256("000000009ed5b82259ecd2aa4cd1f119db8da7a70e7ea78d9c9f603e01f93bcc"));
				blocksToDownload.Add(new uint256("00000000e6da8c2da304e9f5ad99c079df2c3803b49efded3061ecaf206ddc66"));
			}
			else
			{
				throw new NotSupportedNetworkException(network);
			}

			BitcoinStore bitcoinStore = new BitcoinStore();
			await bitcoinStore.InitializeAsync(Path.Combine(Global.Instance.DataDir, EnvironmentHelpers.GetMethodName()), network);

			var addressManagerFolderPath = Path.Combine(Global.Instance.DataDir, "AddressManager");
			var addressManagerFilePath = Path.Combine(addressManagerFolderPath, $"AddressManager{network}.dat");
			var blocksFolderPath = Path.Combine(Global.Instance.DataDir, "Blocks", network.ToString());
			var connectionParameters = new NodeConnectionParameters();
			AddressManager addressManager = null;
			try
			{
				addressManager = await NBitcoinHelpers.LoadAddressManagerFromPeerFileAsync(addressManagerFilePath);
				Logger.LogInfo($"Loaded {nameof(AddressManager)} from `{addressManagerFilePath}`.");
			}
			catch (DirectoryNotFoundException)
			{
				addressManager = new AddressManager();
			}
			catch (FileNotFoundException)
			{
				addressManager = new AddressManager();
			}
			catch (OverflowException)
			{
				File.Delete(addressManagerFilePath);
				addressManager = new AddressManager();
			}
			catch (FormatException)
			{
				File.Delete(addressManagerFilePath);
				addressManager = new AddressManager();
			}

			connectionParameters.TemplateBehaviors.Add(new AddressManagerBehavior(addressManager));
			connectionParameters.TemplateBehaviors.Add(bitcoinStore.CreateMempoolBehavior());

			using var nodes = new NodesGroup(network, connectionParameters, requirements: Constants.NodeRequirements);

			KeyManager keyManager = KeyManager.CreateNew(out _, "password");
			WasabiSynchronizer syncer = new WasabiSynchronizer(network, bitcoinStore, new Uri("http://localhost:12345"), Global.Instance.TorSocks5Endpoint);
			WalletService walletService = new WalletService(
				bitcoinStore,
				keyManager,
				syncer,
				new CoinJoinClient(syncer, network, keyManager, new Uri("http://localhost:12345"), Global.Instance.TorSocks5Endpoint),
				nodes,
				Global.Instance.DataDir,
				new ServiceConfiguration(50, 2, 21, 50, new IPEndPoint(IPAddress.Loopback, network.DefaultPort), Money.Coins(0.0001m)));
			Assert.True(Directory.Exists(blocksFolderPath));

			try
			{
				var mempoolTransactionAwaiter = new EventAwaiter<SmartTransaction>(
							h => bitcoinStore.MempoolService.TransactionReceived += h,
							h => bitcoinStore.MempoolService.TransactionReceived -= h);
				using var mempoolTransactionReceivedTimeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));

				var nodeConnectionAwaiter = new EventAwaiter<NodeEventArgs>(
							h => nodes.ConnectedNodes.Added += h,
							h => nodes.ConnectedNodes.Added -= h);
				using var nodeAddedTimeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));

				nodes.Connect();

				foreach (var hash in blocksToDownload)
				{
					using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
					var block = await walletService.FetchBlockAsync(hash, cts.Token);
					Assert.True(File.Exists(Path.Combine(blocksFolderPath, hash.ToString())));
					Logger.LogInfo($"Full block is downloaded: {hash}.");
				}

				await Task.WhenAll(
					mempoolTransactionAwaiter.Task.WithCancellation(mempoolTransactionReceivedTimeoutCts.Token),
					nodeConnectionAwaiter.Task.WithCancellation(nodeAddedTimeoutCts.Token));
			}
			finally
			{
				// So next test will download the block.
				foreach (var hash in blocksToDownload)
				{
					await walletService?.DeleteBlockAsync(hash);
				}
				if (walletService != null)
				{
					await walletService.StopAsync();
				}

				if (Directory.Exists(blocksFolderPath))
				{
					Directory.Delete(blocksFolderPath, recursive: true);
				}

				IoHelpers.EnsureContainingDirectoryExists(addressManagerFilePath);
				addressManager?.SavePeerFile(addressManagerFilePath, network);
				Logger.LogInfo($"Saved {nameof(AddressManager)} to `{addressManagerFilePath}`.");

				await syncer?.StopAsync();
			}
		}
	}
}
