# AutoParts Hub — Otomobil Yedek Parça Platformu (MVC Final Projesi)

AutoParts Hub; kullanıcıların araç uyumluluğuna göre yedek parça arayıp satın alabildiği, satıcıların ürünlerini yönetip siparişleri takip edebildiği ve adminin tüm sistemi kontrol edebildiği çok rollü bir e-ticaret web uygulamasıdır.

---

- [YouTube Tanıtım Videosu](https://youtu.be/tjNPBbt3gQU)

---

## Proje Amacı

- Araç uyumluluğuna göre doğru yedek parçayı hızlıca bulmayı kolaylaştırmak
- Satıcıların ürün/sipariş/soru süreçlerini tek panelden yönetebilmesini sağlamak
- Admin tarafında araç kataloğu, parçalar, siparişler, kullanıcılar ve mesajların merkezi yönetimini sunmak

---

## Hedef Kullanıcı Kitlesi

- **Müşteriler:** Aracına uygun yedek parça arayan ve satın almak isteyen kullanıcılar
- **Satıcılar:** Ürün listeleyen, stok yöneten, siparişleri güncelleyen satıcılar
- **Admin:** Tüm sistemi yöneten, içerik/veri tutarlılığını kontrol eden yönetici

---

## Senaryo / Kullanım Akışı

1. Kullanıcı ana sayfadan araç uyumluluğu ile arama yapar veya kategoriler/ürün listesinden parça inceler.
2. Ürün detayında teknik bilgiler, uyumluluk listesi ve soru-cevap alanını görür; sipariş oluşturur.
3. Satıcı panelinde kendi ürünlerini yönetir, sipariş durumlarını günceller, kullanıcı sorularını yanıtlar.
4. Admin panelinde araç kataloğu, parça listesi, siparişler, kullanıcılar ve iletişim mesajları yönetilir.

---

## Kullanılan Teknolojiler

- C#
- ASP.NET Core MVC (MVC mimarisi)
- Veritabanı: SQLite
- HTML / CSS / Bootstrap (UI)
---

## Özellikler

### Kullanıcı (Müşteri) Tarafı
- Araç uyumluluk araması (Marka/Model/Yıl filtreleme)
- Ürün listeleme ve filtreleme
- Ürün detay sayfası (teknik özellikler, uyumluluk, değerlendirme, soru-cevap)
- Profil ve sipariş takibi

### Satıcı Paneli
- Satıcı dashboard (satış özetleri, trend grafikleri)
- Ürün yönetimi ve stok güncelleme
- Sipariş yönetimi (durum güncelleme)
- Ürün sorularını yanıtlama

### Admin Paneli
- Araç kataloğu yönetimi (listeleme / ekleme / düzenleme)
- Parça yönetimi (listeleme / ekleme / düzenleme)
- Sipariş yönetimi (filtreleme, durum takibi)
- Kullanıcı & satıcı yönetimi
- İletişim mesajlarını görüntüleme (okundu/yeni)

---

## Ekran Görüntüleri (docs/screenshots)

---

### 1) Kullanıcı Arayüzü — Keşif & Satın Alma

**Ana Sayfa (Araç uyumluluk araması + öne çıkanlar)**
![Ana Sayfa](docs/screenshots/1.png)

**Araçlar Sayfası (marka/model/yıl kataloğu)**
![Satıcı Başvurusu](docs/screenshots/3.png)

**Ürün Listeleme (filtre paneli + kartlar)**
![Tüm Ürünler](docs/screenshots/7.png)

**Ürün Detay (uyumluluk + teknik özellikler + sepete ekle)**
![Ürün Detay](docs/screenshots/27.png)

**Ürün Detay — Soru & Yanıtlar + Değerlendirme + Benzer Ürünler**
![Soru Yanıt ve Değerlendirme](docs/screenshots/28.png)

**Ürün Detay — Benzer Ürünler & Bunlara da göz at**
![Benzer Ürünler](docs/screenshots/29.png)

**Profil Sayfası (hesap bilgileri + kısayollar)**
![Profil](docs/screenshots/9.png)

**Siparişlerim (kargo durumu, beklemede/kargolandı vb.)**
![Siparişlerim](docs/screenshots/8.png)

**İletişim Sayfası (form + iletişim bilgileri)**
![İletişim](docs/screenshots/5.png)

---

### 2) Satıcı — Başvuru & Panel

**Satıcı Başvurusu (Satıcı Ol)**
![Araçlar](docs/screenshots/6.png)


**Satıcı Dashboard (satış özetleri, grafikler, ürün geliri)**
![Satıcı Dashboard](docs/screenshots/10.png)

**Satıcı Ürünlerim (stok arttır/azalt, düzenle, yeni ürün)**
![Satıcı Ürünlerim](docs/screenshots/11.png)

**Satıcı Siparişler (aktif siparişler, durum güncelleme, gör)**
![Satıcı Tamamlanan Siparişler](docs/screenshots/12.png)

**Satıcı Siparişler (tamamlananlar listesi)**
![Satıcı Siparişler](docs/screenshots/21.png)

---

### 3) Admin — Yönetim Paneli

**Admin Dashboard (özet metrikler + grafikler + hızlı işlemler)**
![Admin Dashboard](docs/screenshots/16.png)

**Araç Yönetimi (araç kataloğu listeleme + arama)**
![Araç Yönetimi](docs/screenshots/17.png)

**Yeni Araç Ekle (form + CSV aktarım)**
![Yeni Araç Ekle](docs/screenshots/18.png)

**Parça Listesi (listeleme + stok + düzenleme)**
![Parça Listesi](docs/screenshots/19.png)

**Yeni Parça Ekle (uyumlu araç seçimi, görsel URL/dosya, galeri)**

![Yeni Parça Ekle](docs/screenshots/26.png)

**Siparişler (beklemede/aktif siparişler listesi)**
![Admin Siparişler Aktif](docs/screenshots/20.png)

**Kullanıcılar (user/admin rolleri + satıcılar + onay süreçleri)**
![Kullanıcılar](docs/screenshots/22.png)

**Mesajlar (iletişim formundan gelen mesajlar listesi, yeni/okundu)**
![Mesajlar](docs/screenshots/23.png)

**Mesaj Detay (okundu bilgisi + içerik)**
![Mesaj Detay](docs/screenshots/25.png)

---
