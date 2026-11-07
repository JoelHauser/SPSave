using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace SPSave.Server
{
    /// <summary>
    /// Loads the locks and enables the lock patches. AbstractPatch.Enable only works when called from the patch's own
    /// assembly, so SPSave enables its own patches here rather than through a shared patch list.
    /// </summary>
    [Injectable(InjectionType.Transient, int.MaxValue, TypePriority = OnLoadOrder.PostLoad)]
    public class SPSaveLoader(ISptLogger<SPSaveLoader> logger, ModHelper modHelper, HttpResponseUtil httpResponseUtil, ProfileHelper profileHelper) : IOnLoad
    {
        private static ISptLogger<SPSaveLoader> _logger;
        private static HttpResponseUtil _http;

        internal static void Log(string message) => _logger?.Info("[SPSave] " + message);

        internal static string ErrorBody(BackendErrorCodes code, string message) => _http.GetBody<object>(null, code, message);

        public Task OnLoadAsync(CancellationToken cancellationToken)
        {
            _logger = logger;
            _http = httpResponseUtil;
            LockStore.Load(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), Log);
            RepeatableQuestsFreezePatch.Profiles = profileHelper;

            AbstractPatch[] patches =
            {
                new ItemEventsLockPatch(),
                new RaidStartLockPatch(),
                new RaidEndUnlockPatch(),
                new ScavSaveLockPatch(),
                new PrestigeLockPatch(),
                new ScavRegenerateLockPatch(),
                new WipeUnlockPatch(),
                new RemoveUnlockPatch(),
                new RepeatableQuestsFreezePatch()
            };
            foreach (var patch in patches)
            {
                patch.Enable();
            }
            Log($"Loaded: {patches.Length} lock patches.");
            var pit = PitFireTeamLock.Enable();
            if (pit != null)
            {
                Log(pit + ".");
            }
            return Task.CompletedTask;
        }
    }
}
