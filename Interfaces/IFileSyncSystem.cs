using System.Threading;
using System.Threading.Tasks;

namespace CometPeak.FileSyncSystem {
    public interface IFileSyncSystem {
        public Task StartListening(string currentDirectory, FileSyncSettings settings) => StartListening(currentDirectory, settings, CancellationToken.None);
        public Task StartListening(string currentDirectory, FileSyncSettings settings, CancellationToken cancellation);
        public Task StopListening();
    }
}
