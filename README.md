
# Addressables Prefab & Serialization Uyarıları (Örneklerle)

Bu rehber, uzaktan yüklenen Addressables prefab'larının hangi durumlarda bozulabileceğini ve güvenli değişikliklerin nasıl yapılacağını açıklar. Addressables kullanan script ve UI prefab'larını düzenlerken referans olsun.


## Hızlı Kurallar

- Script GUID: `.meta` dosyalarını kaybetme. Script'in GUID'i değişirse prefab'ta Missing MonoBehaviour oluşur.
- Field Rename: Serileştirilmiş alanları yeniden adlandırırken `FormerlySerializedAs` kullan; yoksa veri kaybolur.
- Field Type: Serileştirilmiş alan tipini değiştirme; yerine yeni alan ekleyip migration yap.
- [SerializeField]: Serileşmesini istediğin alanlardan kaldırma.
- SerializeReference: `[SerializeReference]` grafiğinde kullanılan tipleri yeniden adlandırma/taşıma.
- UnityEvent: Listener metod imzasını veya event parametre tiplerini değiştirme.
- Prefab Children: Alanların referans verdiği child objeleri silip yeniden oluşturma; ID'ler değişir ve referanslar kırılır.
- Root Canvas: Root `Canvas` yerine Canvas'sız child-UI prefab kullan ve sahnedeki Canvas altına parent et.
- Address Keys: String address yerine `AssetReference<T>` tercih et; key değişse bile yükleme kırılmaz.
- Labels/Groups: Loader bir label'a göre çalışıyorsa, label'ı kaldırma/yeniden adlandırma (loader'ı da güncellemeden).


## Güvenli Field Rename

Yeniden adlandırmada veriyi korumak için `FormerlySerializedAs` kullan.

```csharp
using UnityEngine;
using UnityEngine.Serialization;

public class MyBehaviour : MonoBehaviour
{
    [FormerlySerializedAs("myValue")] // old name
    [SerializeField] private int myNewValue; // new name
}
```


## Tip Değiştirmek Yerine Migration

Bir alanın tipini değiştirmek yerine yeni alan ekleyip tek seferlik migration yap.

```csharp
public class MyBehaviour : MonoBehaviour
{
    [SerializeField] private int speedOld; // keep for migration
    [SerializeField] private float speed;   // new field

    [ContextMenu("Migrate Speed")]
    void Migrate()
    {
        if (speed == 0f && speedOld != 0) speed = speedOld;
    # Addressables Prefab & Serialization — El Kitabı

    Güvenle değiştir, kırmadan güncelle. (Teknik terimler İngilizce, açıklamalar Türkçe)

    ---

    ## İçindekiler

    - [1) Script GUID ve .meta (Kırmızı Çizgi)](#1-script-guid-ve-meta-kırmızı-çizgi)
    - [2) Field Rename (Veriyi Korumak)](#2-field-rename-veriyi-korumak)
    - [3) Field Type Change (Tip Değiştirme)](#3-field-type-change-tip-değiştirme)
    - [4) SerializeField Kaldırma (Görünmez Veri Kaybı)](#4-serializefield-kaldırma-görünmez-veri-kaybı)
    - [5) SerializeReference (İsim Sabitliği)](#5-serializereference-isim-sabitliği)
    - [6) UnityEvent (İmza Stabilitesi)](#6-unityevent-imza-stabilitesi)
    - [7) Property ile Serileştirme](#7-property-ile-serileştirme)
    - [8) Prefab Referans Hijyeni](#8-prefab-referans-hijyeni)
    - [9) AssetReference > String Address](#9-assetreference--string-address)
    - [10) UI Image Sprite (Runtime Restore)](#10-ui-image-sprite-runtime-restore)
    - [11) AddressableLoader (Remote Catalog)](#11-addressableloader-remote-catalog)
    - [12) Hızlı HTTP Servis (Yerel)](#12-hızlı-http-servis-yerel)
    - [13) Diagnose & Verify (Ne Bundle'a Girdi?)](#13-diagnose--verify-ne-bundlea-girdi)
    - [14) Content Update (4 Adım)](#14-content-update-4-adım)
    - [15) Hızlı Kontrol Listesi](#15-hızlı-kontrol-listesi)

    ## 1) Script GUID ve .meta (Kırmızı Çizgi)

    Kural: `.meta` dosyasını asla kaybetme; GUID değişirse Prefab → Missing MonoBehaviour.

    Kötü:

    ```text
    Script.cs (sil-yarat)
    Script.cs.meta YOK → GUID değişir
    ```

    İyi:

    ```text
    Script.cs
    Script.cs.meta VCS'te takipte → GUID sabit
    ```

    ---

    ## 2) Field Rename (Veriyi Korumak)

    Kural: Yeniden adlandırırken `FormerlySerializedAs` kullan.

    Kötü:

    ```csharp
    public int myValue; // rename
    public int myNewValue; // data kayıp
    ```

    İyi:

    ```csharp
    using UnityEngine.Serialization;
    [FormerlySerializedAs("myValue")]
    [SerializeField] private int myNewValue;
    ```

    ---

    ## 3) Field Type Change (Tip Değiştirme)

    Kural: Tipi değiştirme; yeni alan ekle, migration yap.

    Kötü:

    ```csharp
    [SerializeField] int speed; // int → float
    ```

    İyi:

    ```csharp
    [SerializeField] int speedOld; // migrate source
    [SerializeField] float speed;   // new

    [ContextMenu("Migrate")]
    void Migrate(){ if(speed==0 && speedOld!=0) speed=speedOld; }
    ```

    ---

    ## 4) [SerializeField] Kaldırma (Görünmez Veri Kaybı)

    Kural: Serileştirmeye devam edecek alanlardan `[SerializeField]` kaldırma.

    Kötü:

    ```csharp
    private int health; // [SerializeField] kaldırıldı
    ```

    İyi:

    ```csharp
    [SerializeField] private int health; // veri korunur
    ```

    ---

    ## 5) SerializeReference (İsim Sabitliği)

    Kural: `[SerializeReference]` altında kullanılan tiplerin class/namespace/assembly adını değiştirme.

    Örnek:

    ```csharp
    [System.Serializable] public class Attack {}

    public class Player : MonoBehaviour
    {
        [SerializeReference] private object behavior = new Attack();
        // Attack adını/namespace'ini değiştirme
    }
    ```

    ---

    ## 6) UnityEvent (İmza Stabilitesi)

    Kural: Event parametrelerini / listener imzalarını değiştirme; inspector bağları bozulur.

    Kötü:

    ```csharp
    public UnityEvent<float> onScore; // önce int'ti
    ```

    İyi:

    ```csharp
    public UnityEvent<int> onScore; // sabit imza
    ```

    ---

    ## 7) Property ile Serileştirme

    Kural: Unity alanları serileştirir; auto-property serileşmez. Backing field kullan.

    Kötü:

    ```csharp
    public int Health { get; set; }
    ```

    İyi:

    ```csharp
    [SerializeField] private int health;
    public int Health { get=>health; set=>health=value; }
    ```

    ---

    ## 8) Prefab Referans Hijyeni

    - Alanların referans verdiği child'ı silip yeniden oluşturma (ID değişir, referans kırılır).
    - Gerekirse yalnızca rename yap veya runtime fallback ekle:

    ```csharp
    public GameObject button;
    void Awake(){
        if(button==null) button = transform.Find("Header/Button")?.gameObject;
    }
    ```

    ---

    ## 9) AssetReference > String Address

    Kural: String key yerine `AssetReference<T>` kullan; key değişse de çalışır.

    Kötü:

    ```csharp
    Addressables.InstantiateAsync("ui/main");
    ```

    İyi:

    ```csharp
    public AssetReferenceGameObject uiPrefab;
    var handle = uiPrefab.InstantiateAsync(parent: transform);
    ```

    ---

    ## 10) UI Image Sprite (Runtime Restore)

    Sahnede Image.sprite koparsa, `AddressableImage` ile runtime yükle.

    ```csharp
    // Component: AddressableImage
    // spriteReference (AssetReferenceSprite) veya addressKey ver
    ```

    ---

    ## 11) AddressableLoader (Remote Catalog)

    - `baseUrlRoot`: ör. `http://localhost:8000/ServerData`
    - Platform klasörü auto-append, son versiyon auto-pick (varsayılan açık)
    - Sadece catalog eklemek için: `labelToLoad` boş, `instantiate=false`
    - Label ile auto-load: `labelToLoad=cdn`, `instantiate=true`

    Doğrulama:

    ```text
    http://localhost:8000/ServerData/StandaloneWindows64/
    catalog_*.json veya latest_catalog.txt görünmeli
    ```

    ---

    ## 12) Hızlı HTTP Servis (Yerel)

    ```powershell
    cd C:\\Users\\Gultekin\\Desktop\\AdressablesFolder
    python -m http.server 8000
    ```

    Unity Ayarı:

    - Addressables Groups → Play Mode Script: Use Existing Build (requires built groups)
    - `AddressableLoader.baseUrlRoot` = `http://localhost:8000/ServerData`
    - Dizin listeleme yoksa: `latest_catalog.txt` ekle (içine katalog dosya adını yaz)

    ---

    ## 13) Diagnose & Verify (Ne Bundle'a Girdi?)

    - Analyze → Build Layout / Bundle Layout Preview
    - `Library/com.unity.addressables/aa/<platform>/BuildLayout.txt`
    - Groups penceresinde Prefab entry kontrolü

    ---

    ## 14) Content Update (4 Adım)

    1. Güvenli değişiklik (tercihen `FormerlySerializedAs`)
    2. Prepare for Content Update (content state seç)
    3. Update a Previous Build
    4. Yeni `catalog_*.json` ve asset'leri remote'a yükle

    ---

    ## 15) Hızlı Kontrol Listesi

    - `.meta` dosyaları VCS'te mi? (GUID sabit)
    - `FormerlySerializedAs` ile rename yapıldı mı?
    - `AssetReference<T>` tercih edildi mi?
    - Prefab child referansları korunuyor mu? (sil-yarat yok)
    - Remote `catalog_*.json` erişilebilir mi?
    - Label/Group ayarları loader ile uyumlu mu?

    ---

    Şüphedeysen: Tip değiştirme → hayır; child sil-yarat → hayır. `FormerlySerializedAs` ile rename, `AssetReference<T>` ile yükleme, `.meta` koruma ile sorunsuz güncelle.

