# Anggota Revit API yang sudah terbukti

Daftar anggota API yang dipakai di repo ini dan **sudah terbukti compile** terhadap
reference assembly Revit 2025 (`Nice3point.Revit.Api.RevitAPI` / `...RevitAPIUI`
versi `2025.4.50`).

Aturannya: sebelum memakai anggota yang belum ada di sini, verifikasi dulu, lalu
tambahkan barisnya. Jangan menulis kode yang "kelihatannya benar".

Cara verifikasi tanpa membuka Revit — refleksi langsung ke reference assembly:

```bash
# lihat seluruh anggota satu type
dotnet run --project tools/ApiProbe -- type ElectricalSystem

# cari nama BuiltInParameter yang mengandung kata tertentu
dotnet run --project tools/ApiProbe -- enum BuiltInParameter RBS_ELEC PREFIX

# cari type berdasarkan potongan nama
dotnet run --project tools/ApiProbe -- find IndependentTag
```

`dotnet build` tetap pemeriksa terakhir: kalau solution compile, seluruh isi tabel
ini benar untuk versi Revit yang tercantum.

## Ringkasan versi

| | |
| --- | --- |
| Revit | 2025 |
| Paket API | `Nice3point.Revit.Api.RevitAPI(UI)` 2025.4.50 |
| Target framework | `net8.0-windows` |

## Circuit dan panel

| Anggota | Catatan |
| --- | --- |
| `ElectricalSystem.Create(Document, IList<ElementId>, ElectricalSystemType)` | Membuat circuit. Argumen kedua daftar `ElementId`, bukan `Element`. |
| `ElectricalSystem.SelectPanel(FamilyInstance)` | Menyambung circuit ke panel. **Ini** yang membuat Revit menetapkan nomor circuit. |
| `ElectricalSystem.CircuitNumber` | `string`, read-only. Satu-satunya sumber nomor seperti `(LC)1`. |
| `ElectricalSystem.PanelName` | `string`, read-only. Kosong berarti circuit belum punya panel → status `no_panel`. |
| `ElectricalSystem.DisconnectPanel()` | Ada, belum dipakai. |
| `ElectricalSystemType.PowerCircuit` | Nilai enum yang dipakai untuk lighting dan receptacle. |
| `MEPModel.GetElectricalSystems()` | `ISet<ElectricalSystem>` — circuit yang memuat elemen ini. |
| `MEPModel.GetAssignedElectricalSystems()` | Dipakai pada panel untuk menghitung slot terpakai. |
| `MEPModel.ConnectorManager` | Bisa `null` untuk family tanpa connector. |
| `Connector.Domain` → `Domain.DomainElectrical` | Dasar penentuan status `no_connector`. |

**Tidak ada** `ElectricalSystem.BaseEquipment` di 2025 — kelayakan panel dibaca dari
`PanelName`, bukan dari properti itu.

## Parameter

| `BuiltInParameter` | Dipakai untuk |
| --- | --- |
| `RBS_ELEC_APPARENT_LOAD` | `device.va`, dikonversi lewat `UnitTypeId.VoltAmperes` |
| `RBS_ELEC_PANEL_NAME` | `panel.name` |
| `RBS_ELEC_CIRCUIT_PREFIX` | `panel.prefix`, hidup di **type** panel |
| `RBS_FAMILY_CONTENT_DISTRIBUTION_SYSTEM` | `panel.distribution_system`; `None` → `is_usable = false` |
| `RBS_ELEC_VOLTAGE` | `panel.voltage` |
| `RBS_ELEC_PANEL_NUMPHASES_PARAM` | `panel.phase` |
| `RBS_ELEC_MAX_POLE_BREAKERS` | `panel.slots_total`, hidup di **type** panel |
| `RBS_ELEC_CIRCUIT_NUMBER` | Parameter yang dibaca family tag |

## Kategori

`OST_LightingFixtures`, `OST_ElectricalFixtures`, `OST_ElectricalEquipment`,
`OST_LightingFixtureTags`, `OST_ElectricalFixtureTags`.

## Tag

| Anggota | Catatan |
| --- | --- |
| `IndependentTag.Create(Document, ElementId symId, ElementId ownerDBViewId, Reference, bool addLeader, TagOrientation, XYZ)` | Overload yang dipakai. Ada overload lain dengan `TagMode`. |
| `IndependentTag.GetTaggedLocalElementIds()` | Dipakai untuk tidak menempatkan tag dobel. |
| `TagOrientation.Horizontal` | |

## Extensible Storage

| Anggota | Catatan |
| --- | --- |
| `SchemaBuilder(Guid)` + `SetSchemaName` / `SetVendorId` / `SetReadAccessLevel` / `SetWriteAccessLevel` / `AddSimpleField` / `Finish()` | Membangun schema pengikatan project. |
| `Schema.Lookup(Guid)` | `null` kalau schema belum pernah dibuat di sesi ini. |
| `Entity(Schema)`, `Entity.Get<T>(string)`, `Entity.Set(string, T)`, `Entity.IsValid()` | |
| `Element.GetEntity(Schema)` / `SetEntity(Entity)` / `DeleteEntity(Schema)` | Dipakai di `doc.ProjectInformation`. |

`SetWriteAccessLevel(AccessLevel.Vendor)` mengikat penulisan ke `VendorId` yang sama
dengan `CircuitSync.addin`. Kalau keduanya tidak cocok, penulisan ditolak saat jalan —
bukan saat compile.

## Satuan

| Anggota | Catatan |
| --- | --- |
| `UnitUtils.ConvertFromInternalUnits(double, ForgeTypeId)` | |
| `UnitUtils.ConvertToInternalUnits(double, ForgeTypeId)` | |
| `UnitTypeId.Millimeters`, `UnitTypeId.VoltAmperes` | |

## Elemen dan identitas

| Anggota | Catatan |
| --- | --- |
| `ElementId.Value` (`long`) | **Bukan** `IntegerValue`, yang deprecated sejak 2024. |
| `Element.UniqueId` (`string`) | Satu-satunya identitas yang disimpan ke Supabase. |
| `Document.GetElement(string uniqueId)` | `null` = elemen sudah dihapus; dilaporkan, tidak dilempar. |
| `Element.LevelId`, `Element.GetTypeId()`, `Element.Category.BuiltInCategory` | |
| `FamilyInstance.Symbol.Family.Name`, `FamilyInstance.Symbol.Name` | Sumber `family_key`. |
| `FamilyInstance.Room` | Bisa melempar kalau family tidak punya Room Calculation Point. |
| `FamilyInstance.Host` | Fallback level untuk fixture yang di-host ceiling. |
| `LocationPoint.Point` | Koordinat device. |

## Transaksi

| Anggota | Catatan |
| --- | --- |
| `TransactionGroup(Document, string)`, `Start()`, `Assimilate()` | Satu group per operasi apply → satu Ctrl+Z. |
| `Transaction`, `SubTransaction`, `HasStarted()`, `HasEnded()`, `RollBack()` | Satu SubTransaction per circuit. |
| `Document.Regenerate()` | Dipanggil setelah `SelectPanel` supaya `CircuitNumber` sudah terisi saat dibaca. |

## UI

| Anggota | Catatan |
| --- | --- |
| `IExternalApplication`, `IExternalCommand`, `IExternalEventHandler` | |
| `ExternalEvent.Create(IExternalEventHandler)`, `ExternalEvent.Raise()` | Satu-satunya jalan memanggil API dari luar thread utama. |
| `UIControlledApplication.CreateRibbonTab(string)` | Melempar `Autodesk.Revit.Exceptions.ArgumentException` kalau tab sudah ada. |
| `UIControlledApplication.CreateRibbonPanel(string, string)` | |
| `PushButtonData(string, string, string, string)`, `RibbonPanel.AddItem(...)` | `AddItem` mengembalikan `RibbonItem`; perlu cast ke `PushButton`. |
| `PushButton.LargeImage`, `PushButton.Image`, `PushButton.ToolTip` | |
| `UIApplication.MainWindowHandle` | Owner window WPF supaya panel tidak tenggelam di belakang Revit. |
| `UIApplication.Application.VersionNumber` | Ditampilkan di subtitle panel. |
| `UIApplication.ActiveUIDocument` | Bisa `null` kalau tidak ada dokumen terbuka. |
