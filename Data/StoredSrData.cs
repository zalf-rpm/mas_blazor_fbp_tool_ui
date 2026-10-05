using Blazored.LocalStorage;

namespace Mas.Infrastructure.BlazorComponents;

public class StoredSrData : IComparable<StoredSrData>
{
    public ulong InterfaceId { get; set; }
    public string SturdyRef { get; set; } = "";
    public string PetName { get; set; } = "";
    public bool AutoConnect { get; set; }
    public bool DefaultSelect { get; set; }

    public static string StorageKey { get; set; } = "sturdy-ref-store";

    public int CompareTo(StoredSrData? other)
    {
        // A null value means that this object is greater.
        if (other == null)
            return 1;
        if (PetName != "" && other.PetName != "")
            return string.Compare(PetName, other.PetName, StringComparison.Ordinal);
        return string.Compare(SturdyRef, other.SturdyRef, StringComparison.Ordinal);
    }

    public StoredSrData Clone()
    {
        return new StoredSrData
        {
            InterfaceId = InterfaceId,
            SturdyRef = SturdyRef,
            PetName = PetName,
            AutoConnect = AutoConnect,
            DefaultSelect = DefaultSelect,
        };
    }

    public static async Task<List<StoredSrData>> GetAllData(ILocalStorageService service)
    {
        return await service.GetItemAsync<List<StoredSrData>>(StorageKey) ?? [];
    }

    public static async Task<List<StoredSrData>> SaveNew(
        ILocalStorageService service,
        StoredSrData newData
    )
    {
        var all = await GetAllData(service);
        all.Add(newData);
        return await SaveAllData(service, all);
    }

    public static async Task<List<StoredSrData>> SaveAllData(
        ILocalStorageService service,
        List<StoredSrData> allData
    )
    {
        await service.SetItemAsync(StorageKey, allData);
        return allData;
    }
}
