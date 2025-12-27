namespace PKTWinNode.Services
{
    public interface ISchedulerService : IDisposable
    {
        void Start();
        void Stop();
    }
}
