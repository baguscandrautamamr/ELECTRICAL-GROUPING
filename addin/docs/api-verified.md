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

### Mengubah circuit yang sudah ada

Circuit yang isinya diubah dari web dibongkar lalu dibuat ulang — bukan ditambal.

| Anggota | Catatan |
| --- | --- |
| `Document.Delete(ElementId)` | Membongkar `ElectricalSystem` lama sebelum dibuat ulang. Mengembalikan `ICollection<ElementId>`; hasilnya tidak dipakai. |

Alasan memilih bongkar-pasang: `ElectricalSystem.AddToCircuit` / `RemoveFromCircuit`
akan mempertahankan nomor circuit, tapi keduanya **belum diverifikasi** terhadap
reference assembly 2025 dan tidak dipakai di repo ini. Konsekuensi yang diterima:
nomor hasil pembangunan ulang bisa berbeda, karena `SelectPanel` memilih slot kosong
sendiri. Kalau nomor yang tetap ternyata penting, verifikasi dua anggota itu dulu
lewat `tools/ApiProbe` sebelum menggantinya.

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
| `SCHEDULE_LEVEL_PARAM` | Level family berbasis face, yang tidak punya `LevelId` sendiri |

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

## View denah

Dipakai untuk membaca layout kerja — lihat `ModelReader.ReadLayouts`.

| Anggota | Catatan |
| --- | --- |
| `ViewPlan` | Di-collect lewat `OfClass(typeof(ViewPlan))`. Mencakup floor plan dan ceiling plan. |
| `View.IsTemplate` | View template ikut ter-collect dan harus dibuang. |
| `ViewPlan.GenLevel` | `Level`, bisa `null` untuk denah yang tidak terikat level. |
| `View.Scale` | `int`, penyebut skala: 1:100 → `100`. |
| `View.CropBoxActive` | `bool`. False berarti crop tidak dipakai; `CropBox` tetap punya nilai tapi tidak berarti. |
| `View.CropBox` | `BoundingBoxXYZ`. |
| `BoundingBoxXYZ.Min` / `.Max` / `.Transform` | Min dan Max ada di koordinat kotak, bukan koordinat model. |
| `Transform.OfPoint(XYZ)` | Memindahkan titik crop ke koordinat model. Tanpa ini denah yang diputar meleset. |
| `FilteredElementCollector(Document, ElementId viewId)` | Overload yang menyaring persis seperti yang terlihat di view: filter view, visibility kategori, crop region, fase. Dasar isi `layout_devices`. |

Overload per-view itu yang membedakan denah lighting dari denah emergency/exit di
lantai yang sama. Melempar `Autodesk.Revit.Exceptions.ArgumentException` untuk view
yang tidak mendukung pengumpulan; ditangani sebagai "isinya tidak diketahui", bukan
sebagai kegagalan tarikan model.

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
| `LocationCurve.Curve` + `Curve.Evaluate(double, bool)` | Fixture berbasis garis tidak punya `LocationPoint`; titik tengahnya diambil di parameter 0,5 ternormalisasi. |
| `Element.get_BoundingBox(View)` | Dipanggil dengan `null` = kotak di koordinat model. Jaring terakhir untuk device tanpa location. |
| `XYZ.Add(XYZ)`, `XYZ.Multiply(double)` | Dipakai menghitung pusat bounding box. Metode eksplisit, bukan operator. |

Ketiganya ada supaya device tanpa `LocationPoint` tidak jatuh ke titik 0,0 — di web
hasilnya setumpuk simbol yang saling menindih di pojok denah.

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
| `UIControlledApplication.ControlledApplication` | Jalan masuk ke event tingkat aplikasi. |
| `ControlledApplication.DocumentChanged` (event) | Dipakai menandai model berubah supaya tarikan berikutnya terkirim sendiri. |
| `DocumentChangedEventArgs.GetDocument()` / `GetAddedElementIds()` / `GetModifiedElementIds()` / `GetDeletedElementIds()` | `Autodesk.Revit.DB.Events`. Id yang dihapus tidak bisa di-resolve — elemennya sudah tidak ada. |

`DocumentChanged` berjalan pada **setiap** transaksi Revit, termasuk milik add-in lain.
Handler-nya harus murah dan tidak boleh melempar: exception dari sana muncul sebagai
dialog error Revit di tengah pekerjaan user.
