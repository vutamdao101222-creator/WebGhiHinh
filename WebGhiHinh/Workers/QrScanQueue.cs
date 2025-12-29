using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;
using WebGhiHinh.Models; // Namespace chứa QrScanRequest

namespace WebGhiHinh.Workers
{
    // 1. Định nghĩa Interface (để Controller gọi)
    public interface IQrScanQueue
    {
        void Enqueue(QrScanRequest item);
        ValueTask<QrScanRequest> DequeueAsync(CancellationToken ct);
    }

    // 2. Thực thi hàng đợi (Dùng Channel cho hiệu suất cao nhất)
    public class QrScanQueue : IQrScanQueue
    {
        private readonly Channel<QrScanRequest> _queue;

        public QrScanQueue()
        {
            // Giới hạn hàng đợi 100 items để tránh tràn RAM
            var options = new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<QrScanRequest>(options);
        }

        public void Enqueue(QrScanRequest item)
        {
            _queue.Writer.TryWrite(item);
        }

        public ValueTask<QrScanRequest> DequeueAsync(CancellationToken ct)
        {
            return _queue.Reader.ReadAsync(ct);
        }
    }
}