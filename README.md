# Ostriv Translation Studio

**Windows x64 · Ukrainian-source localization editor · English / Беларуская / Русский interface**

Documentation language: **[English](#english)** · **[Беларуская](#беларуская)** · **[Русский](#русский)**

> **Important:** Ostriv Translation Studio is an unofficial community tool. It does not include the game or replace the original game executable. Use it with a legally obtained copy of Ostriv. The program targets the Ukrainian language slot; it does not add new entries to the game's language menu.

---

## English

### Overview

Ostriv Translation Studio is a Windows x64 desktop editor for localizing **Ostriv**. It supports English, Belarusian, and Russian **application interfaces** and multiple **target translation languages** in one project. The Ukrainian source text remains on the left; you edit the selected target language on the right. Changing the application's interface language does not change or overwrite the translation you are working on.

A **Belarusian machine-translated draft for proofreading** has been prepared for all **2,087 confirmed entries**. If the draft project and CSV are distributed separately, you can open the project or import the CSV into your own project. Unconfirmed string candidates are kept separate. The draft is not a reviewed, final translation.

### Quick start

**Prebuilt application:** download the release ZIP, extract its **entire contents**, and launch `OstrivTranslate.exe` from the extracted directory. The published build does not require a separate .NET installation.

1. Select **Extract from EXE**, choose the **original** `ostriv.exe` from your installed game, and add a target language, for example `be` / `Беларуская`, `ru` / `Русский`, or `pl` / `Polski`.
2. Select an entry in the table. The Ukrainian original is shown on the left; enter the translation in the lower-right editor. An empty translation keeps the original text in the generated EXE.
3. Save with **Save project** or `Ctrl+S`. Use `Ctrl+Shift+S` for **Save As**, and `Ctrl+F` to focus the search field.
4. Select **+ Language** to maintain a second or subsequent target language in the same project. Switching the application's interface language does not erase translations.
5. Click **Build EXE…** and **enter the output filename yourself**, such as `ostriv_bel.exe`. Saving beside the original game is recommended. Each build contains **one selected target language**.
6. Click **Launch build**. In Ostriv, select the **Ukrainian language slot**. You can change its displayed name through the `@locale@` entry; the tool does not create additional language-menu entries.

**Selecting multiple entries:** select the first and `Shift`-click the last to select a range; `Ctrl`-click toggles individual entries. `Ctrl+A` in the table selects all entries in the current search/filter result. With multiple entries selected, single-entry editors are hidden and a shared confirmation control applies to selected candidates. A mixed state means only some are confirmed: one click confirms all selected candidates, and unchecking withdraws their **manual** confirmation. This does not modify previously confirmed regular entries or translation text.

### Build modes and text safety

| Mode | Behavior |
| --- | --- |
| **Extended** (default) | Longer translations are moved into a new `.otrl` PE section, and supported references are redirected. Existing sections are not shifted. The builder validates modified ranges, addresses, and the resulting file. Short translations are patched in place. |
| **Strict** | The UTF-16LE translation must not exceed the original byte length; unused bytes are zeroed and the NUL terminator is preserved. The EXE size is unchanged. |

Length checks use **UTF-16 code units and bytes**, not visual character counts: characters outside the Basic Multilingual Plane may use two UTF-16 code units. Unicode support does not guarantee that all glyphs exist in the game's fonts or that the renderer handles complex scripts (such as Arabic joining). The editor reports missing glyphs for each font, but actual rendering and layout must be checked in-game.

Preserve format placeholders and the **order of recognized markup tags**. Embedded NUL characters and invalid surrogate pairs are rejected. Translations are never silently truncated. Relocation is permitted only when the relevant references are supported and pass validation.

### Extraction and coverage

For the verified executable, the extractor identifies **2,087 confirmed entries** using paired Ukrainian and English text references in the program, including long help texts. An additional scan finds **1,958 candidates**; these may include names and localizable strings, but also technical data and false positives. These figures do **not** establish complete coverage of every visible string in the game.

Confirmed entries are shown by default. Use the corresponding filter to inspect candidates. Candidates must be manually confirmed in their entry view before inclusion. Confirmation records the user's review of the candidate's intended purpose; it does not prove that patching is safe. Interior references and overlapping shared string suffixes block automatic replacement even after confirmation. Longer replacements also require supported references.

Identical text at different offsets is listed as separate entries. Where multiple locations point to the same in-memory string, patching affects all references; the entry view shows reference count and merged context.

**Verified original `ostriv.exe` SHA-256:**

```text
448f226f40cf4a2bf591818a8d2604ee1cb470aeb0ee2adc3f37a9e4aa448779
```

Other executable versions may still be scanned and edited, but **building is locked** until a verified profile is created for that version. Offsets from older builds are not reused for newer ones.

### Projects, CSV, and game updates

A `*.ostriv.json` project stores the source-file hash, original strings, contexts, candidate confirmations, and translations for each target language. Project saves are atomic; replaced projects receive a uniquely suffixed `.bak-…` backup. Output EXEs are also written atomically with backups. Each build produces a neighboring `*.exe.build.json` manifest containing hashes and the change list.

CSV is UTF-8 with a BOM and uses this header:

```csv
id,language,original,translation
```

Export includes all project entries and target languages. Before import, a preview displays new translations, conflicts, and unknown entry IDs. Conflicts are deselected by default. CSV import never imports offsets, trust/confidence data, or code references. Strings beginning with `=` are preserved literally; when opening CSV in spreadsheet software, import the translation column **as text** to avoid formula interpretation.

After an Ostriv update, select **Rescan / Migrate**. Entries matching both original text and context are proposed automatically; ambiguous matches require manual selection. Unmatched entries remain visible for review. Migration to a new executable creates a **new project**, preserving previous work. It does **not** automatically create a new verified build profile.

### Safety and limitations

The tool does not overwrite original game files. Output-path checks account for Windows hard links and symbolic links. It does not modify Steam. A differently named executable may still depend on Steam, and compatibility with future Ostriv versions is not guaranteed. **Back up your translation project and game saves before experimenting.**

Game executables, extracted game assets, and extracted localization projects are **not included** in the release package. The release contains the editor and applicable dependency-license notices. Translation material based on Ostriv's original texts may have separate rights and redistribution restrictions; an open-source license for this editor does not grant rights to the game's content.

### Development and validation

For source builds you need **Windows x64, .NET SDK 10**, and NuGet access for initial package restore. Open `TranslateOstriv.slnx` in Visual Studio or run:

```powershell
.\build.ps1 -Test -Publish
```

To run integration tests against an installed game, enter the path to your own original EXE:

```powershell
$gameExe = Read-Host "Full path to original ostriv.exe"
.\build.ps1 -Test -GameExe $gameExe
dotnet run --project .\tests\OstrivTranslate.Tests -- $gameExe
```

The test project is an executable test suite without a separate test framework. Without an accessible game EXE, JSON, CSV, Unicode, and file-operation tests run, while integration tests are **explicitly skipped**.

For optional WPF smoke verification, open PowerShell in the directory containing the published application and provide a valid settings file:

```powershell
.\OstrivTranslate.exe --settings ".\settings.json" --smoke ".\smoke"
```

This check does not launch Ostriv or write to the game directory. It uses the game path from the settings or the standard installation location.

**Source layout:** `src/OstrivTranslate.Core` — extraction, PE handling, validation, projects, CSV, and building; `src/OstrivTranslate.App` — WPF, interface localization, and font checks; `tests/OstrivTranslate.Tests` — tests, including installed-game integration checks. See [docs/VALIDATION.md](docs/VALIDATION.md) for technical details and validation limits.

### Open-source licensing

**Suggested license for the editor's original source code: MIT.** Before publishing, place the completed MIT license text in a root-level `LICENSE` file with the correct copyright holder. This is a **recommendation**, not a claim that the repository is already licensed. Third-party dependencies retain their own licenses; the license does not cover Ostriv or its copyrighted game resources. See the Russian section below and [Choose a License](https://choosealicense.com/licenses/mit/) for details.

[Back to language selection](#ostriv-translation-studio)

---

## Беларуская

### Пра праграму

Ostriv Translation Studio — настольны рэдактар лакалізацыі **Ostriv** для Windows x64. Інтэрфейс праграмы даступны **па-беларуску, па-руску і па-англійску**. У адным праекце можна захоўваць пераклады на некалькі моў. Украінскі арыгінал заўсёды паказваецца злева, а справа рэдагуецца пераклад на выбраную мову. Змена мовы інтэрфейсу не змяняе і не сцірае пераклады.

Для вычыткі падрыхтаваны **чарнавы машынны пераклад на беларускую мову** ўсіх **2 087 пацверджаных запісаў**. Калі праект перакладу і CSV распаўсюджваюцца асобна, можна адкрыць праект або імпартаваць CSV ва ўласны праект. Непацверджаныя кандыдаты захоўваюцца асобна. Машынны чарнавік не з'яўляецца канчатковым правераным перакладам.

### Хуткі пачатак

**Гатовая праграма:** спампуйце ZIP-архіў выпуску, распакуйце **ўсё яго змесціва** і запусціце `OstrivTranslate.exe` з распакаванай папкі. Асобная ўсталёўка .NET для апублікаванай зборкі не патрэбная.

1. Націсніце **«Выняць з EXE»**, выберыце **арыгінальны** `ostriv.exe` з папкі ўсталяванай гульні і дадайце мову перакладу, напрыклад `be` / `Беларуская`, `ru` / `Русский` або `pl` / `Polski`.
2. Выберыце запіс у табліцы. Злева знаходзіцца ўкраінскі арыгінал, справа ўнізе — поле рэдагавання перакладу. Калі пакінуць пераклад пустым, пры зборцы застанецца арыгінальны тэкст.
3. Захоўвайце праект кнопкай **«Захаваць праект»** або `Ctrl+S`. `Ctrl+Shift+S` — **«Захаваць як»**, `Ctrl+F` — пераход да пошуку.
4. Каб дадаць яшчэ адну мову, націсніце **«+ Мова»**. Пераключэнне мовы інтэрфейсу не ўплывае на змесціва перакладаў.
5. Націсніце **«Сабраць EXE…»** і **самастойна ўвядзіце назву файла**, напрыклад `ostriv_bel.exe`. Рэкамендуецца захоўваць яго побач з арыгінальнай гульнёй. Кожная зборка змяшчае **адну выбраную мову перакладу**.
6. Націсніце **«Запусціць зборку»**. У гульні выберыце **ўкраінскі моўны слот**. Назву слота можна змяніць праз запіс `@locale@`, але новыя пункты выбару мовы ў меню гульні не дадаюцца.

**Множнае вылучэнне:** выберыце першы запіс, а затым націсніце апошні з утрыманай клавішай `Shift`, каб вылучыць дыяпазон. `Ctrl` + пстрычка дадае ці прыбірае асобны запіс. `Ctrl+A` у табліцы вылучае ўсе запісы, якія адпавядаюць бягучаму пошуку і фільтру. Калі вылучана некалькі запісаў, палі адной карткі хаваюцца, а агульная адзнака пацвярджэння прымяняецца да ўсіх выбраных кандыдатаў. Змешаны стан азначае, што пацверджана толькі частка запісаў: адна пстрычка пацвердзіць усіх выбраных кандыдатаў, а зняцце адзнакі скасуе іх **ручное** пацвярджэнне. Звычайныя пацверджаныя запісы і тэксты перакладаў пры гэтым не змяняюцца.

### Рэжымы зборкі і бяспека тэксту

| Рэжым | Паводзіны |
| --- | --- |
| **Пашыраны** (прадвызначаны) | Доўгія пераклады размяшчаюцца ў новай PE-секцыі `.otrl`, а падтрыманыя спасылкі перанакіроўваюцца. Наяўныя секцыі не зрушваюцца. Зборшчык правярае змененыя дыяпазоны, адрасы і выніковы файл. Кароткія пераклады замяняюцца на месцы. |
| **Строгі** | Даўжыня перакладу ў UTF-16LE не павінна перавышаць даўжыню арыгінала ў байтах. Вольныя байты абнуляюцца, нулявы тэрмінатар захоўваецца, а памер EXE не змяняецца. |

Абмежаванні вылічваюцца ў **адзінках UTF-16 і байтах**, а не ў візуальных сімвалах: сімвалы па-за базавай шматмоўнай плоскасцю могуць займаць дзве адзінкі UTF-16. Падтрымка Unicode не гарантуе наяўнасці патрэбных гліфаў у гульнявых шрыфтах або карэктнага злучэння літар у складаных сістэмах пісьма (напрыклад, у арабскай). Рэдактар паказвае адсутныя гліфы для кожнага шрыфту, але выгляд тэксту і яго размяшчэнне трэба правяраць у гульні.

Параметры фарматавання і **парадак распазнаных тэгаў** неабходна захоўваць. Убудаваны NUL і некарэктныя сурагатныя пары забаронены. Пераклады не абразаюцца аўтаматычна. Перанос радкоў у новую секцыю дапускаецца толькі пры наяўнасці падтрымаваных спасылак і паспяховай праверцы.

### Выманне і паўната

Для праверанай версіі EXE праграма знаходзіць **2 087 пацверджаных запісаў** праз пары ўкраінскага і англійскага тэксту ў кодзе, у тым ліку доўгія даведкавыя апісанні. Дадатковае сканаванне знаходзіць **1 958 кандыдатаў** — сярод іх могуць быць назвы і тэксты для перакладу, але таксама тэхнічныя радкі і памылковыя супадзенні. Гэта **не доказ поўнага ахопу** ўсіх надпісаў гульні.

Па змаўчанні паказваюцца пацверджаныя запісы. Для прагляду кандыдатаў выберыце адпаведны фільтр. Кандыдата трэба пацвердзіць у яго картцы перад уключэннем у зборку. Пацвярджэнне азначае, што карыстальнік праверыў прызначэнне радка, але не гарантуе бяспекі замены. Спасылкі ўнутр радка і перакрыццё агульных заканчэнняў блакіруюць аўтаматычную замену нават пасля пацвярджэння. Для даўжэйшых перакладаў патрэбныя падтрыманыя спасылкі.

Аднолькавыя радкі па розных адрасах паказваюцца асобна. Калі некалькі месцаў выкарыстоўваюць адзін радок у памяці, змяненне закранае ўсе яго спасылкі. Колькасць спасылак і аб'яднаны кантэкст бачны ў картцы запісу.

**SHA-256 праверанага арыгінальнага `ostriv.exe`:**

```text
448f226f40cf4a2bf591818a8d2604ee1cb470aeb0ee2adc3f37a9e4aa448779
```

Для іншых версій EXE можна шукаць кандыдатаў і рэдагаваць пераклады, але **зборка заблакіраваная**, пакуль для гэтай версіі не будзе створаны правераны профіль. Старыя зрушэнні не выкарыстоўваюцца для новых EXE.

### Праекты, CSV і абнаўленні гульні

Праект `*.ostriv.json` захоўвае хэш зыходнага файла, арыгінальныя радкі, кантэксты, пацвярджэнні кандыдатаў і пераклады для кожнай мовы. Захаванне праекта атамарнае: пры замене ствараецца рэзервовая копія з унікальным суфіксам `.bak-…`. Выніковыя EXE таксама запісваюцца атамарна з рэзервовымі копіямі. Побач ствараецца файл `*.exe.build.json` з хэшамі і пералікам змен.

CSV захоўваецца ў UTF-8 з BOM і мае загаловак:

```csv
id,language,original,translation
```

Экспарт уключае ўсе запісы і мовы перакладу ў праекце. Перад імпартам паказваюцца новыя пераклады, канфлікты і невядомыя ідэнтыфікатары. Канфлікты па змаўчанні не выбраны. З CSV **не імпартуюцца** зрушэнні, звесткі пра давер да радка і спасылкі на яго ў кодзе. Пераклады, якія пачынаюцца з `=`, захоўваюцца літаральна; пры адкрыцці CSV у таблічным рэдактары імпартуйце слупок перакладу **як тэкст**, каб пазбегнуць інтэрпрэтацыі формул.

Пасля абнаўлення Ostriv выкарыстоўвайце **«Перасканаваць / перанесці»**. Супадзенні арыгінальнага радка і кантэксту прапануюцца аўтаматычна; неадназначныя супадзенні патрабуюць ручнога выбару. Нясупастаўленыя запісы застаюцца даступнымі для праверкі. Для новай версіі гульні ствараецца **новы праект**, таму ранейшыя пераклады не губляюцца. Новы правераны профіль зборкі **не ствараецца аўтаматычна**.

### Бяспека і абмежаванні

Праграма не перазапісвае арыгінальныя файлы гульні. Пры праверцы шляхоў улічваюцца жорсткія і сімвалічныя спасылкі Windows. Steam не змяняецца. EXE з іншай назвай можа па-ранейшаму залежаць ад Steam, а сумяшчальнасць з будучымі версіямі Ostriv не гарантуецца. **Перад эксперыментамі зрабіце рэзервовыя копіі праекта перакладу і захаванняў гульні.**

У выпуск не ўваходзяць EXE гульні, вынятыя гульнявыя рэсурсы і праекты, вынятыя з арыгінала. Пакет змяшчае рэдактар і неабходныя звесткі пра ліцэнзіі залежнасцей. Тэксты перакладаў, створаныя на аснове арыгінальных матэрыялаў Ostriv, могуць мець асобныя абмежаванні на распаўсюджванне. Ліцэнзія з адкрытым зыходным кодам для рэдактара не дае правоў на змесціва гульні.

### Распрацоўка і праверкі

Для зборкі з зыходнікаў патрэбныя **Windows x64, .NET SDK 10** і доступ да NuGet для першага аднаўлення пакетаў. Адкрыйце `TranslateOstriv.slnx` у Visual Studio або выканайце:

```powershell
.\build.ps1 -Test -Publish
```

Каб выканаць інтэграцыйныя тэсты з усталяванай гульнёй, укажыце шлях да свайго арыгінальнага EXE:

```powershell
$gameExe = Read-Host "Шлях да арыгінальнага ostriv.exe"
.\build.ps1 -Test -GameExe $gameExe
dotnet run --project .\tests\OstrivTranslate.Tests -- $gameExe
```

Тэставы праект — выканальны набор праверак без асобнага тэставага фрэймворка. Без даступнага EXE запускаюцца праверкі JSON, CSV, Unicode і файлавых аперацый, а інтэграцыйныя тэсты **яўна прапускаюцца**.

Для дадатковай праверкі WPF адкрыйце PowerShell у папцы апублікаванай праграмы і ўкажыце сапраўдны файл налад:

```powershell
.\OstrivTranslate.exe --settings ".\settings.json" --smoke ".\smoke"
```

Гэта праверка не запускае гульню і нічога не запісвае ў яе папку. Шлях да EXE бярэцца з налад або стандартнага размяшчэння гульні.

**Структура зыходнікаў:** `src/OstrivTranslate.Core` — выманне, апрацоўка PE, праверкі, праекты, CSV і зборка; `src/OstrivTranslate.App` — WPF, лакалізацыя інтэрфейсу і праверка шрыфтоў; `tests/OstrivTranslate.Tests` — тэсты, у тым ліку інтэграцыйныя з усталяванай гульнёй. Тэхнічныя падрабязнасці і межы праверак — у [docs/VALIDATION.md](docs/VALIDATION.md).

### Ліцэнзія з адкрытым зыходным кодам

**Рэкамендаваная ліцэнзія для ўласнага зыходнага кода рэдактара — MIT.** Перад публікацыяй дадайце ў корань рэпазіторыя запоўнены файл `LICENSE` з правільным праваўладальнікам. Гэта **рэкамендацыя**, а не сцвярджэнне, што рэпазіторый ужо мае такую ліцэнзію. Залежнасці захоўваюць свае ліцэнзіі; правы на Ostriv і гульнявыя рэсурсы не перадаюцца. Падрабязней: [Choose a License](https://choosealicense.com/licenses/mit/).

[Да выбару мовы](#ostriv-translation-studio)

---

## Русский

### О программе

Ostriv Translation Studio — настольный редактор локализации **Ostriv** для Windows x64. Интерфейс доступен **на английском, белорусском и русском языках**. В одном проекте можно хранить переводы на несколько языков. Украинский оригинал отображается слева, справа редактируется выбранный язык перевода. Переключение языка интерфейса не меняет и не удаляет переводы.

Подготовлен **белорусский машинный черновик для вычитки** всех **2 087 подтверждённых записей**. Если проект перевода и CSV распространяются отдельно, можно открыть проект или импортировать CSV в собственный. Неподтверждённые кандидаты хранятся отдельно. Машинный черновик не считается окончательной проверенной локализацией.

### Быстрый запуск

**Готовое приложение:** скачайте ZIP выпуска, распакуйте **всё содержимое** и запустите `OstrivTranslate.exe` из распакованной папки. Отдельная установка .NET опубликованной сборке не требуется.

1. Нажмите **«Извлечь из EXE»**, выберите **оригинальный** `ostriv.exe` из папки установленной игры и добавьте язык перевода, например `be` / `Беларуская`, `ru` / `Русский` или `pl` / `Polski`.
2. Выберите запись в таблице. Слева находится украинский оригинал, справа внизу — поле редактирования перевода. Если оставить перевод пустым, при сборке сохранится оригинальный текст.
3. Сохраняйте работу кнопкой **«Сохранить проект»** или `Ctrl+S`. `Ctrl+Shift+S` — **«Сохранить как»**, `Ctrl+F` — переход к поиску.
4. Для добавления ещё одного языка нажмите **«+ Язык»**. Смена языка интерфейса не затрагивает содержимое переводов.
5. Нажмите **«Собрать EXE…»** и **самостоятельно введите имя файла**, например `ostriv_bel.exe`. Рекомендуется сохранить его рядом с оригинальной игрой. Каждая сборка включает **один выбранный язык перевода**.
6. Нажмите **«Запустить сборку»**. В игре выберите **украинский языковой слот**. Его отображаемое название можно изменить через запись `@locale@`, но новые пункты выбора языка в меню игры не добавляются.

**Множественное выделение:** выберите первую строку и нажмите на последнюю с `Shift`, чтобы выделить диапазон. `Ctrl` + щелчок добавляет или убирает отдельную запись. `Ctrl+A` в таблице выделяет все записи текущего поиска и фильтра. При множественном выборе поля одной записи скрываются, а общий переключатель подтверждения применяется ко всем выбранным кандидатам. Смешанное состояние означает, что подтверждена только часть записей: один щелчок подтвердит всех выбранных кандидатов, снятие отметки отменит их **ручное** подтверждение. Обычные подтверждённые записи и тексты переводов при этом не меняются.

### Режимы сборки и безопасность текста

| Режим | Поведение |
| --- | --- |
| **Расширенный** (по умолчанию) | Длинные переводы размещаются в новой PE-секции `.otrl`, а поддерживаемые ссылки перенаправляются. Существующие секции не сдвигаются. Сборщик проверяет изменённые диапазоны, адреса и записанный результат. Короткие переводы заменяются на месте. |
| **Строгий** | Перевод в UTF-16LE не превышает исходный размер в байтах; свободные байты обнуляются, нулевой терминатор сохраняется. Размер EXE не изменяется. |

Лимит рассчитывается в **единицах UTF-16 и байтах**, а не в визуальных символах: символ за пределами базовой многоязычной плоскости может занимать две единицы UTF-16. Поддержка Unicode не гарантирует наличия всех глифов в шрифтах игры или корректного соединения букв в сложных системах письма (например, арабском). Редактор показывает отсутствующие глифы по каждому шрифту, но оформление и размещение текста следует проверять в самой игре.

Плейсхолдеры и **порядок распознанных тегов** необходимо сохранять. Встроенный NUL и некорректные суррогатные пары запрещены. Переводы не обрезаются автоматически. Перенос строки в новую секцию разрешён только при наличии поддерживаемых ссылок и успешной проверке.

### Извлечение и полнота

Для проверенной сборки извлекаются **2 087 подтверждённых записей** по парам украинского и английского текста в коде, включая длинные страницы справки. Дополнительное сканирование находит **1 958 кандидатов**: среди них могут быть названия и локализуемые строки, но также технические данные и ложные совпадения. Эти цифры **не доказывают полноту охвата** всех текстов игры.

По умолчанию показаны подтверждённые записи. Для просмотра кандидатов используйте соответствующий фильтр. Перед включением кандидата требуется ручное подтверждение в его карточке. Подтверждение означает, что пользователь проверил назначение строки, но не гарантирует безопасность её замены. Ссылки внутрь строк и перекрывающиеся общие окончания блокируют автоматическую подмену даже после подтверждения. Для удлинения переводов обязательно наличие поддерживаемых ссылок.

Одинаковые тексты по разным адресам представлены отдельно. Если несколько мест используют одну строку в памяти, изменение затрагивает все её ссылки; количество ссылок и объединённый контекст отображаются в карточке.

**SHA-256 поддерживаемого оригинального `ostriv.exe`:**

```text
448f226f40cf4a2bf591818a8d2604ee1cb470aeb0ee2adc3f37a9e4aa448779
```

Для других версий EXE доступны сканирование кандидатов и редактирование, но **сборка заблокирована** до создания проверенного профиля для соответствующей версии. Смещения старого EXE к новому не применяются.

### Проекты, CSV и обновления игры

Проект `*.ostriv.json` хранит хеш оригинала, исходные строки, контексты, подтверждения кандидатов и переводы по языкам. Проект сохраняется атомарно; при замене создаётся резервная копия с уникальным суффиксом `.bak-…`. Итоговый EXE также сохраняется атомарно с резервной копией. Рядом формируется `*.exe.build.json` с хешами и перечнем изменений.

CSV используется в UTF-8 с BOM и со столбцами:

```csv
id,language,original,translation
```

Экспорт включает все записи и языки проекта. Перед импортом отображаются новые переводы, конфликты и неизвестные идентификаторы. Конфликты по умолчанию не выбраны. Смещения, сведения о доверии к строке и ссылки из CSV не импортируются. Переводы, начинающиеся с `=`, сохраняются буквально: при открытии CSV в табличном редакторе импортируйте столбец перевода **как текст**, чтобы избежать интерпретации формул.

После обновления Ostriv используйте **«Пересканировать / перенести»**. Совпадения по оригинальному тексту и контексту предлагаются автоматически, неоднозначные совпадения требуют ручного выбора. Несопоставленные записи остаются доступными в окне проверки. При смене версии создаётся **новый проект**, чтобы прежние переводы сохранились. Новый проверенный профиль сборки **не создаётся автоматически**.

### Безопасность и ограничения

Программа не заменяет оригинальные файлы игры. Проверка идентичности путей учитывает жёсткие и символические ссылки Windows. Steam не изменяется. Отдельное имя EXE не гарантирует независимость от Steam или совместимость с будущими версиями Ostriv. **Перед экспериментами сделайте резервные копии проекта перевода и сохранений игры.**

В выпуск не входят EXE игры, извлечённые игровые ресурсы и проекты, сформированные из оригинала. Пакет содержит редактор и необходимые уведомления о лицензиях зависимостей. Переводы, основанные на оригинальных текстах Ostriv, могут иметь отдельные ограничения на распространение. Открытая лицензия на редактор не предоставляет прав на содержимое игры.

### Разработка и проверки

Для сборки из исходников нужны **Windows x64, .NET SDK 10** и доступ к NuGet для первого восстановления пакетов. Откройте `TranslateOstriv.slnx` в Visual Studio или выполните:

```powershell
.\build.ps1 -Test -Publish
```

Для интеграционных тестов с установленной игрой укажите путь к собственному оригинальному EXE:

```powershell
$gameExe = Read-Host "Путь к оригинальному ostriv.exe"
.\build.ps1 -Test -GameExe $gameExe
dotnet run --project .\tests\OstrivTranslate.Tests -- $gameExe
```

Тестовый проект — исполняемый набор проверок без отдельного тестового фреймворка. Если EXE игры недоступен, выполняются проверки JSON, CSV, Unicode и файловых операций; интеграционные тесты **явно пропускаются**.

Для дополнительной WPF-проверки откройте PowerShell в каталоге опубликованного приложения и укажите действительный файл настроек:

```powershell
.\OstrivTranslate.exe --settings ".\settings.json" --smoke ".\smoke"
```

Эта проверка не запускает игру и не записывает файлы в её каталог. Путь к EXE берётся из настроек или стандартного расположения игры.

**Структура исходников:** `src/OstrivTranslate.Core` — извлечение, обработка PE, проверки, проекты, CSV и сборка; `src/OstrivTranslate.App` — WPF, локализация интерфейса и проверка шрифтов; `tests/OstrivTranslate.Tests` — тесты, в том числе интеграционные с установленной игрой. Технические подробности и границы проверок приведены в [docs/VALIDATION.md](docs/VALIDATION.md).

### Лицензирование открытого проекта

**Рекомендуемая лицензия для собственного исходного кода редактора — MIT.** Перед публикацией добавьте в корень репозитория заполненный `LICENSE` с правильным правообладателем. Это **рекомендация**, а не утверждение, что проект уже распространяется под MIT. Сторонние зависимости сохраняют свои лицензии; права на Ostriv и игровые ресурсы не передаются. Подробнее: [Choose a License](https://choosealicense.com/licenses/mit/).

[К выбору языка](#ostriv-translation-studio)
