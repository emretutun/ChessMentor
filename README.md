# ♞ ChessMentor: Türkçe Satranç Koçun

**ChessMentor**, bilgisayara karşı oynarken yanında oturan bir satranç hocası gibi çalışan, tamamen **ücretsiz** ve **internetsiz** bir masaüstü uygulamasıdır. Amaç sadece "doğrusu buydu" demek değil, **oyun görüşü kazandırmak**: tehditleri fark etmeyi, kritik anları sezmeyi ve kendi hatalarından öğrenmeyi.

> C# / .NET 10 / .NET MAUI · Motor: [Stockfish](https://stockfishchess.org) (ücretsiz, açık kaynak) · Yapay zekâ/LLM gerekmez · 0 ₺ maliyet

---

## ✨ Özellikler

| | Özellik | Açıklama |
|---|---|---|
| 🤖 | **Ayarlanabilir rakip** | Başlangıç (≈600) seviyesinden Tam güç Stockfish'e kadar 10 seviye. Bot, insan gibi hissettirmek için kısa bir "düşünme" süresi kullanır. |
| ✅ | **Her hamleye geri bildirim** | Her hamlen *Harika / En iyi / Mükemmel / İyi / Yanlışlık / Hata / Ciddi hata* olarak sınıflandırılır. |
| 🗣️ | **Türkçe açıklamalar** | *"Rakip Fxe5 ile e5'teki atı alabilir."*, *"💡 Ad5 çatal yapıyordu: c7'deki kale ve e7'deki vezir aynı anda tehdit altındaydı."* |
| 🤔 | **Önce sen düşün modu** | Kritik anlarda ve rakibin tehdidi olduğunda cevabı hemen söylemez, önce seni düşündürür. |
| 🔁 | **Hatada dur** | Hata yaptığında bot cevap vermeden durur. *Tekrar dene* ile geri alıp daha iyisini arayabilir ya da *Devam et* ile doğru hamleyi görebilirsin. |
| 💡 | **Kademeli ipucu** | 1) Pozisyonun karakteri → 2) Hangi taş → 3) Fikir/motif → 4) Hamlenin kendisi (tahtada okla). |
| ⚠️ | **Rakibin tehdidi** | "Sıra rakipte olsaydı ne yapardı?" analiziyle rakibin tehdidini bulur. |
| 📊 | **Değerlendirme çubuğu** | Kazanma şansını canlı gösterir. |
| 🏁 | **Oyun sonu raporu** | Doğruluk yüzdesi, hamle dağılımı, en büyük 3 hata ve kişisel tavsiye. |
| 🎯 | **Hata kalıbı takibi** | En sık yaptığın hata türünü (taş bırakmak, çatal kaçırmak, mata izin vermek…) takip eder ve çalışma tavsiyesi verir. |
| 📋 | **PGN dışa aktarma** | Oyunu Lichess veya Chess.com analizine yapıştırabilirsin. |
| 🇹🇷 | **Kolay okunur hamleler** | Varsayılan olarak `Af3` yerine **"At g1 → f3"**, `O-O` yerine **"Kısa rok"** yazılır. Ayarlar'dan Türkçe kısaltmaya (Af3) veya uluslararası notasyona (Nf3) geçilebilir. |
| ♟️ | **Alınan taşlar** | Oyuncu isimlerinin yanında alınan taşlar ve materyal farkı (+2 gibi). |

---

## 🚀 Kurulum

### Gereksinimler
- Windows 10 (1809+) veya Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- .NET MAUI iş yükü: Visual Studio Installer'da **".NET Multi-platform App UI development"** seçili olmalı ya da:

```bash
dotnet workload install maui-windows
```

### 1) Stockfish'i indir (tek seferlik)

Proje klasöründe şu betiği çalıştır. İşlemcine uygun resmi Stockfish sürümünü GitHub'dan indirir ve `engine/stockfish.exe` olarak kurar:

```bash
powershell -ExecutionPolicy Bypass -File scripts\get-stockfish.ps1
```

> Elle kurmak istersen: [stockfishchess.org/download](https://stockfishchess.org/download/) adresinden Windows sürümünü indir, `.exe` dosyasını `engine/` klasörüne koy ya da uygulamada **Ayarlar → Gözat…** ile seç.

### 2) Çalıştır

```bash
dotnet run --project src/ChessMentor.App -f net10.0-windows10.0.19041.0
```

Ya da `ChessMentor.slnx` dosyasını Visual Studio 2022/2026 ile aç, başlangıç projesi olarak **ChessMentor.App**'i seç ve **F5**'e bas.

### Testler

```bash
dotnet test
```

Testler arasında hamle üretiminin doğruluğunu kanıtlayan standart **perft** testleri de var (başlangıç pozisyonu, Kiwipete vb.).

---

## 🎮 Nasıl kullanılır?

1. Sağ panelden **rakip seviyesini** ve **rengini** seç, **▶ Oyunu başlat**'a bas.
2. Taşa tıkla, yasal kareler noktayla gösterilir. Gitmek istediğin kareye tıkla.
3. Her hamlenden sonra koç kartında değerlendirmeyi oku. Önceki notlar **💬 Koçun notları** sekmesinde.
4. Takıldığında **💡 İpucu**'na bas. Her basışta ipucu biraz daha açılır.
5. Oyun bittiğinde raporu incele, **📊 İstatistiklerim** sekmesinde gelişimini takip et.

**Tahtadaki renkler:**
- 🟨 Sarı: son hamle · 🟩 Yeşil: seçili taş · 🟦 Mavi: ipucu
- **Yeşil ok:** daha iyi olan hamle · **Mavi ok:** ipucu · **Kırmızı ok:** rakibin tehdidi

---

## 🧠 Nasıl çalışır?

### İki motor
Uygulama **iki ayrı Stockfish süreci** çalıştırır:

```
              ┌──────────────────────────┐
  Sen  ⇄  Tahta (MAUI)  ⇄  Oyun kuralları (ChessMentor.Core)
              └────────────┬─────────────┘
                           │
          ┌────────────────┴────────────────┐
   Rakip motor (zayıflatılmış)      Analiz motoru (tam güç)
   UCI_Elo / Skill Level            MultiPV 3, her pozisyon
          │                                 │
     Botun hamlesi              Mentor: sınıflandırma, açıklama,
                                tehdit, ipucu, rapor
```

Böylece rakip 1200 seviyesinde oynarken mentor 3000+ seviyesinde düşünür. Motorla iletişim **UCI protokolüyle** yapılır: `stockfish.exe` bir alt süreç olarak başlatılır, komutlar stdin'e yazılır, cevaplar stdout'tan okunur.

### Hamle sınıflandırma
Motor puanı (santipiyon), Lichess'in kullandığı formülle **kazanma şansına** çevrilir:

```
kazanma% = 50 + 50 · (2 / (1 + e^(−0.00368 · cp)) − 1)
```

Hamleden önceki ve sonraki kazanma şansı farkına göre:

| Kazanma şansı kaybı | Sınıf |
|---|---|
| Motorun ilk tercihi (kritik anda) | 🌟 Harika |
| Motorun ilk tercihi | ✅ En iyi |
| < %2 | 👍 Mükemmel |
| < %5 | 🙂 İyi |
| < %10 | ⚠️ Yanlışlık |
| < %20 | ❌ Hata |
| ≥ %20 | 💥 Ciddi hata |

Doğruluk yüzdesi: `103.1668 · e^(−0.04354 · kayıp) − 3.1669`

### Türkçe açıklamalar (LLM'siz)
Hangi hamlenin iyi olduğuna **her zaman motor karar verir**. Açıklama katmanı, motorun bulduğu varyantları basit taktik tespitleriyle insan diline çevirir:

- Korumasız / tehlikedeki taş
- Çatal (bir taşın aynı anda iki veya daha fazla değerli hedefi tehdit etmesi)
- Açmaz
- Mat tehdidi ve mat fırsatı
- Varyant boyunca materyal kazancı

Bu sayede açıklamalar hızlıdır, internet gerektirmez ve **asla uydurma bilgi içermez**.

### Rakibin tehdidi
"Sıra rakipte olsaydı ne yapardı?" sorusunu sormak için motor, sırası rakibe verilmiş pozisyonu (null move) kısa süre analiz eder. Rakibin bu durumdaki kazancı belirgin şekilde yüksekse bu bir tehdittir.

### Kritik an
Analizde en iyi hamle ile ikinci en iyi hamle arasında büyük fark varsa (**tek iyi hamle** durumu), mentor seni uyarır.

---

## 📁 Proje yapısı

```
ChessMentor/
├─ src/
│  ├─ ChessMentor.Core/     Satranç kuralları: tahta, FEN, yasal hamleler, SAN/PGN, Türkçe ekler
│  ├─ ChessMentor.Engine/   UCI motor sarmalayıcı (Stockfish), seviye ayarları, motor bulucu
│  ├─ ChessMentor.Mentor/   Sınıflandırma, Türkçe açıklama, ipucu, tehdit, rapor, istatistik
│  └─ ChessMentor.App/      .NET MAUI arayüzü (tahta çizimi, mentor paneli, istatistik, ayarlar)
├─ tests/ChessMentor.Tests/ Perft ve mentor mantığı testleri (xUnit)
├─ scripts/get-stockfish.ps1
└─ engine/                  Stockfish buraya iner (git'e dahil edilmez)
```

**Core**, **Engine** ve **Mentor** katmanları arayüzden tamamen bağımsızdır (saf `net10.0` kütüphaneleri). İleride başka bir arayüze (web, mobil) geçildiğinde aynen kullanılabilirler. Motor `IChessEngine`, açıklayıcı `IMoveExplainer` arayüzünün arkasındadır.

---

## 🗺️ Yol haritası

- [ ] Taşları sürükle-bırak ile oynama
- [ ] Hamle sesleri
- [ ] Kendi hatalarından üretilen kişisel bulmacalar
- [ ] Oyun geçmişi ve kayıtlı oyunları tekrar izleme
- [ ] Açılış kitabı ve açılış adları
- [ ] Android desteği (Stockfish'in ARM derlemesiyle `IChessEngine` uygulaması)
- [ ] İnsansı rakip için [Maia](https://maiachess.com) ağları (lc0 ile)
- [ ] (İsteğe bağlı) [Ollama](https://ollama.com) ile yerel, ücretsiz LLM açıklamaları (`IMoveExplainer`)

---

## 📜 Lisans

- ChessMentor kaynak kodu: [MIT](LICENSE)
- **Stockfish** ayrı bir programdır ve [GPLv3](https://github.com/official-stockfish/Stockfish/blob/master/Copying.txt) lisanslıdır. Bu depo Stockfish'i **içermez**.

- **Taş görselleri** (`src/ChessMentor.App/Resources/Pieces`): Colin M.L. Burnett'in "cburnett" seti, [Wikimedia Commons](https://commons.wikimedia.org/wiki/Category:SVG_chess_pieces) üzerinden. GFDL / BSD / GPL / CC BY-SA 3.0 lisanslarından herhangi biri altında kullanılabilir.

