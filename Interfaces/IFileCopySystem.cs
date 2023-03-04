using System.Threading;
using System.Threading.Tasks;

namespace CometPeak.FileCopier {
    public interface IFileCopySystem {
        public Task StartListening(string currentDirectory, FileCopySettings settings) => StartListening(currentDirectory, settings, CancellationToken.None);
        public Task StartListening(string currentDirectory, FileCopySettings settings, CancellationToken cancellation);
        public Task StopListening();
    }
}
