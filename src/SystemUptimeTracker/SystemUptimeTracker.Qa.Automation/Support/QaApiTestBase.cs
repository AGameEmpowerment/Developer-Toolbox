namespace SystemUptimeTracker.Qa.Automation.Support
{
    public abstract class QaApiTestBase : QaTestBase
    {
        protected override string[] CreateHostArgs()
        {
            return [];
        }

        protected virtual Task OnBeforeHostCreated()
        {
            return Task.CompletedTask;
        }

        protected virtual void OnHostCreationFailed()
        {
        }

        protected virtual void OnHostReady()
        {
        }

        protected virtual void OnAfterHostDisposed()
        {
        }

        protected override async Task OnOneTimeSetUp()
        {
            try
            {
                await OnBeforeHostCreated();
                OnHostReady();
            }
            catch
            {
                OnHostCreationFailed();
                throw;
            }
        }

        protected override Task OnOneTimeTearDown()
        {
            OnAfterHostDisposed();
            return Task.CompletedTask;
        }
    }
}
