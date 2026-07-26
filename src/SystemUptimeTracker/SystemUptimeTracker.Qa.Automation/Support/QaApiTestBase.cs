namespace SystemUptimeTracker.Qa.Automation.Support
{
    public abstract class QaApiTestBase : QaTestBase
    {
        protected override string[] CreateHostArgs()
        {
            return [];
        }

        protected virtual void OnBeforeHostCreated()
        {
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

        protected override Task OnOneTimeSetUp()
        {
            try
            {
                OnBeforeHostCreated();
                OnHostReady();
                return Task.CompletedTask;
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
