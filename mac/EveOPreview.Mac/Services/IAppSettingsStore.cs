using System;
using System.Threading;
using System.Threading.Tasks;

namespace EveOPreview.Mac.Services;

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default);
}
