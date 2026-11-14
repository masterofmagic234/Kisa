# Передача переноса на Unity — Codex на ПК пользователя

Состояние на 14 ноября 2026 года. Это продолжение начатого переноса, а не
готовая Unity-версия двух игр. Пользователь поручил перенести **и весь Acorn
Hunter, и основу White Guard**, затем прямо разрешил Codex на своём ПК
установить Unity и закончить работу. Установка на ПК ещё не выполнялась.

## Откуда продолжать

| Назначение | Репозиторий и ветка | Проект / документ |
|---|---|---|
| Общий пакет и White Guard | `masterofmagic234/Kisa`, `feat/unity-migration` | `unity/WhiteGuard`, `unity/Packages/com.arkon.runtime` |
| Полный Acorn, приватный | `masterofmagic234/Arkon`, `feat/unity-migration` | `unity/AcornHunter`, `unity/HANDOFF_ACORN_UNITY.md` |

- [Kisa PR #3](https://github.com/masterofmagic234/Kisa/pull/3).
- [Arkon PR #16](https://github.com/masterofmagic234/Arkon/pull/16), private.
- Прочитать `AGENTS.md` в локальном workspace и `Kisa/unity/AGENTS.md`.
- Снимок реализации до этого хэндоффа: Kisa
  `2eb6de1dfc8dd24f1c58f7c3482ff2ec7cf0233f`; Arkon
  `cda7a8573d79b73c2d86dc40b926bcba5a4c8516`.
- После хэндоффа HEAD веток новее. Получить **текущие** ветки, не откатывать
  их к этим снимкам. Сохранить существующие незакоммиченные изменения.
- Точную зависимость Acorn сверять по `Arkon/unity/Migration/shared-runtime.json`
  и `AcornHunter/Packages/manifest.json`: они должны указывать один commit Kisa.

У отсутствующих локальных копий можно выполнить `git clone --branch
feat/unity-migration https://github.com/masterofmagic234/Kisa.git` и аналогично
для Arkon с настроенным доступом пользователя. Для существующих копий сначала
проверить status/ветку/remote и получить обновления; не применять reset/clean.

## Что уже написано и чем это подтверждено

| Область | Состояние |
|---|---|
| Core C# 9 / .NET Standard 2.1 | Здоровье, оружие, input latch, top-down motor, AI, двери, A*, предметы, пулы и projectile policy |
| Unity 2D | Реальные queries/body/projectiles, `TopdownWorld2D`, AI/door/pickup adapters, живые transition/combat gates, door slam |
| 3D | Публичный FirstPersonActor: движение/взгляд/гравитация, stair planner и CharacterController/capsule adapter; полный Level 1 не перенесён |
| Гонка | Трансмиссия, reverse interlock, track/projection/contact math; полного controller/AI/renderer ещё нет |
| White Guard | `Assets/Scenes/Opening.unity`, bootstrap и PNG/MP3 player; актуальная постановка schema 3 |
| Acorn | Приватный Unity workspace, закреплённый общий пакет и инвентаризация; **0 исходных игровых сцен перенесено** |

На исходной машине прошли **5558 assertions** переносимой логики. Часть из них
сравнивает исполнение настоящих Godot-компонентов, а не C# с самим собой.
Для вступления проверены 363 момента всех 84 планов: 1021 команда PNG draw,
144 отражения; максимальная разница координат менее 0,001 пикселя.
Компиляция runtime/Editor-кода против 139 проверенных managed DLL официального
Unity 6000.3.28f1 прошла с 0 warnings / 0 errors. GitHub portable CI прошёл.

Отчёты: `unity/Validation/core-parity.json`, `unity-api-compilation.json`,
`migration-status.json`. Это **не** результаты Unity Editor, GPU, декодера,
PhysX/Physics2D или готового Player: такие проверки ещё **не запускались**.
Подготовленные ниже native-команды тоже пока не исполнены.

## Сохранить авторский материал

Вступление White Guard берёт постановку из Godot-коммита
`e63c20e53aebac24022796ec583a83db9a9e3adb`: 84 плана, 176 art-регионов,
17 pose-наборов, 11 formations. Изображения, текст, звук и шрифты копируются
из `white_guard` без изменения содержимого; всего 41 файл в media manifest.
Монолог Мазура — **1018 слов, 12 MP3, 472,344 секунды**.

Пользователь назвал прежнюю рисовку идеальной и критиковал режиссуру. Не
возвращать отвергнутую векторную перерисовку, не сокращать и не переозвучивать
монолог. Перенос двигателя не является новым художественным заданием.
White Guard сейчас ограничен вступлением/основой: не придумывать остальную
кампанию книги. Полный Acorn, напротив, входит в объём переноса.

Карта и материалы полного Acorn, приватный source inventory и его подробный
хэндофф остаются в Arkon. Не переносить в публичный Kisa приватные игровые
файлы, личные архивы, ключи, VPN-данные или полный текст книги.

## 1. Подготовить ПК и установить Unity

1. Определить реальную ОС, архитектуру, свободный диск, GPU/драйвер и имеющиеся
   Git/Python/.NET/Hub/Editor. Пути `/workspace`, `.tools/dotnet` и извлечённые
   Cloud DLL на ПК отсутствуют; найти свои пути. Старое ограничение Cloud-диска
   к ПК автоматически не относится.
2. Проверить место под Editor, модули, два `Library` и сборки. Linux Editor в
   прежней среде занимал бы 8,14 GiB без проектов. Практический ориентир запаса
   на ПК — 30 GiB; принимать решение по реальным размерам выбранных модулей.
3. Установить Git, Python 3.10+ и .NET 10 SDK, если отсутствуют. Для portable
   CI использовался SDK 10.0.404. Unity использует собственный компилятор;
   .NET 10 нужен тестовому executable, а runtime остаётся .NET Standard 2.1.
4. Установить официальный [Unity Hub](https://unity.com/download) и Editor
   **Unity 6.3 LTS, 6000.3.28f1, revision `8f082d2fe476`**. Не обновлять версию
   проекта молча и не создавать пустой проект вместо имеющегося.
5. Активировать доступную пользователю лицензию обычным способом. Unity
   Personal активируется через Hub. Если вход/2FA/лицензия или системный UAC
   требуют участия пользователя, остановить только зависимый шаг и коротко
   объяснить конкретное действие; не запрашивать пароль или ключ в чате.
6. Git должен быть в PATH процесса Unity: приватный проект разрешает общий
   UPM-пакет по Git URL. Добавить в Hub оба существующих каталога проектов.

Официальная [страница версии](https://unity.com/releases/editor/whats-new/6000.3.28f1),
[release API](https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.3.28f1),
[активация](https://docs.unity.com/en-us/engine/6000.3/manual/get-started/install-and-upgrade/licenses-and-activation/license-activation-methods).

Для автоматизации есть новый [Unity CLI](https://docs.unity.com/en-us/unity-cli/use-unity-cli)
([команды Editor](https://docs.unity.com/en-us/unity-cli/reference/editor-project-commands)).
Сначала проверить фактическую версию и `--help`. Текущие команды установки:

```text
unity install 6000.3.28f1 -c 8f082d2fe476
unity editors -i --json
```

Для существующего **Linux** build method на Windows/macOS добавить модуль
`linux-mono` (`unity install ... -m linux-mono` либо `unity install-modules -e
6000.3.28f1 -m linux-mono` у Hub/CLI-managed установки). На Linux Mono уже
встроен. Для первого имеющегося build IL2CPP не требуется. Установка CLI не
заменяет активацию Personal в Hub. Старый `Hub --headless install` не считать
универсальной командой: Hub 4 его больше не поддерживает.

На Windows с Codex в WSL запускать Windows Editor из подходящего Windows
каталога SSD; согласовать Windows-путь проекта с путём, видимым Codex. Не
передавать Windows Unity выдуманный Linux-путь `/workspace/...`.

## 2. Проверить checkout и переносимые тесты

Из корня Kisa, используя найденный Python:

```text
python unity/Tools/validate_layout.py
python unity/Tools/check_core.py --dotnet dotnet
```

Ожидание для текущего кода: PASS, 41 byte-identical файла, schema 3 / 84 плана,
1018 слов и 5558 portable assertions. Число проверок может обоснованно расти
после исправлений. Godot не нужен для воспроизведения уже записанных fixtures.
Для свежего исполнения источника — команды export в `unity/README.md`.

В хэндофф добавлены Windows-исправления `as_posix()` для GUID/manifest и
`.gitattributes` для сохранения LF/source bytes. Предпочтителен свежий clone
после этого коммита. Атрибуты сами не переписывают уже существующий CRLF
worktree. При старом checkout сначала сохранить пользовательские правки, затем
восстановить только проверенно неизменённые файлы с нужными окончаниями строк.
Не подгонять golden SHA/manifest под CRLF и не делать общий reset.

В этой Cloud-среде проверены совпадение GUID через `PureWindowsPath` и
пробный Git checkout с `core.autocrlf=true` / `core.eol=crlf`: все 151
source/media/API hash совпали. Это проверка переносимости данных на Linux,
не исполнение Windows или Unity; отчёт `Validation/pc-handoff-checks.json`.

`prepare_white_guard.py` повторно копирует исходные материалы и создаёт сцену/
настройки проекта. Использовать его при осознанном обновлении источника; после
редактирования Unity-сцены не перезапускать без проверки diff, чтобы не затереть
новую работу. Существующие GUID должны сохраняться на любой ОС.

В PowerShell проверять `$LASTEXITCODE` у Python/dotnet: одного
`$ErrorActionPreference='Stop'` недостаточно для native-процессов в PS 5.1.

## 3. Впервые исполнить проверки в настоящем Editor

Открыть `Kisa/unity/WhiteGuard`, дождаться завершения импорта/UPM и исправить
реальные compiler/import errors. Сначала сохранить полный Editor.log; не
объявлять проверку успешной по отсутствию окна ошибки.

Ниже `Unity` обозначает полный путь настоящего Editor executable, а не Unity
CLI. Пути логов и проекта абсолютные. Выполнять последовательно с ожиданием
процесса, проверять exit code и содержимое каждого лога. Не открывать один
проект одновременно GUI-редактором и batch-процессом.

```text
Unity -batchmode -nographics -quit -projectPath <Kisa>/unity/WhiteGuard -executeMethod Arkon.Unity.Editor.MigrationChecks.VerifyIntroImports -logFile <logs>/intro-import.log
Unity -batchmode -nographics -quit -projectPath <Kisa>/unity/WhiteGuard -executeMethod Arkon.Unity.Editor.MigrationChecks.VerifyNativePhysics -logFile <logs>/native-physics.log
Unity -batchmode -nographics -projectPath <Kisa>/unity/WhiteGuard -executeMethod Arkon.Unity.Editor.PhysicsLifecycleChecks.Run -logFile <logs>/native-lifecycle.log
```

На Windows использовать ожидание `Start-Process -Wait -PassThru` с корректно
заключёнными в кавычки путями и проверкой `.ExitCode`, либо Python
`subprocess.run([editor, ...], check=True)` без shell. Простого запуска GUI exe
из интерактивного PowerShell недостаточно для достоверного результата.

**К lifecycle-команде не добавлять `-quit`.** Она сама входит в PlayMode,
создаёт временную сцену, проверяет FixedUpdate, inactive-world/spawn refusal,
заморозку игрока при transition и disable→enable→spawn внутри impact callback,
выгружает сцену и выходит с кодом результата. Это не тест всех AI/дверей/ступеней.

На машине с настоящим Editor не нужно заново скачивать Cloud-набор DLL, чтобы
имитировать API-проверку. Реальная компиляция и native-запуск теперь приоритетны.

## 4. Визуально и на слух принять вступление

В графическом Editor открыть **`Assets/Scenes/Opening.unity`**, нажать Play.
Сначала показывается титульная карточка; Enter/Space запускает вступление.
Проверить полный проход 472,344 секунды и 12 аудиоблоков, затем отдельно:

- шрифты/кириллицу, кадрирование, порядок слоёв, reveal, mirror, позы и колёса;
- синхронизацию с реальным звуком, смену MP3 и завершение последнего блока;
- Enter/Space pause/resume, Escape pause, потерю фокуса, global pause;
- mute/captions, skip/restart, отключение и включение объекта с сохранением
  позиции голоса, разные размеры окна и letterboxing.

Сравнить с текущим Godot-источником, не перерисовывая материалы. Числовой oracle
не проверяет GPU blending, glyph metrics, аудиолатентность и качество режиссуры.
Записать видео/скриншоты и найденные отличия. Это первый настоящий графический
запуск, результат заранее неизвестен.

## 5. Native gameplay, полный Acorn и сборки

1. По `Runtime/Unity/World/PREFAB_WIRING.md` собрать небольшие временные
   регрессионные сцены для реальной физики. Проверить wall slide, удар дверью
   при 84 px/s, занятую закрывающуюся дверь, медпакет под стоящим игроком, AI
   у угла, перестройку навигации после disable/enable, transition и два
   additive world. Тестовые сцены не выдавать за перенесённый уровень.
2. Проверить 3D-движение/взгляд/пол/потолок и ступени на PhysX. Публичный
   planner исполняет up/forward/down; высота .20 m — предел чистого подъёма,
   а не произвольный запрет любого препятствия выше .20 m. Округлая капсула
   в Godot могла принять частичный шаг у .25 m riser. Проверить по fixture.
3. Продолжить полную приватную игру по `Arkon/unity/HANDOFF_ACORN_UNITY.md`:
   сначала настоящий Level 3 как первый игровой срез, затем остальные режимы,
   глобальные сервисы и все маршруты. Не создавать новую сюжетную White Guard
   кампанию под видом переноса Acorn.
4. Для первой сборки текущего вступления есть метод
   `Arkon.Unity.Editor.MigrationChecks.BuildLinuxIntro`. Он жёстко задаёт
   **StandaloneLinux64 / Development** и только `Opening.unity`. Сначала
   отдельно пройти lifecycle/GUI-проверки: build method их не запускает.
   `ARKON_UNITY_BUILD` — путь к выходному executable, не просто каталогу.
5. Под ОС ПК добавить/использовать подходящий build target и метод, собрать
   и реально запустить Player. Linux-бинарник, собранный на Windows, не считать
   проверенным запуском Windows. После переноса Acorn собрать и пройти его
   кампанию, а не только вступление White Guard.

Важные контракты: Core без UnityEngine; 2D source pixels/y-down/radians,
100 px на Unity unit; source masks 1/2/4/8 соответствуют layer indices
8/9/10/11. У врага 1 HP / 0 invulnerability, у игрока 100 HP / .24 s.
Player projectile damage принадлежит callback/volley cache, enemy direct damage
8 не дублировать callback. Существующий Physics2D bridge рассчитан на default
PhysicsScene2D, не на отдельный LocalPhysicsMode.Physics2D.

## Завершение работы и доказательства

После каждого этапа фиксировать реальные ошибки и исправления, версию Editor,
ОС/железо, tested commit, команды/exit codes, логи и результаты ручного прохода.
Большие логи/видео хранить вне source tree; коммитить краткий воспроизводимый
отчёт и нужные regression tests. Обновлять migration status только по факту.

Общий пакет менять в Kisa; приватные карты/миссии/медиа — в Arkon. После
проверенного изменения пакета запушить Kisa и закрепить точный новый commit
в двух private manifest/status файлах. Для совместной локальной отладки можно
временно заменить Git dependency на корректный `file:` путь относительно
`Arkon/unity/AcornHunter/Packages`, затем вернуть переносимый Git pin перед push.

Работу нельзя считать законченной, пока White Guard opening и **все исходные
маршруты Acorn** не импортированы, не работают в Editor и собранных Players,
а gameplay/pause/retry/transitions/save/audio/UI не проверены на реальных сценах.
Не подменять эти результаты portable assertions, заглушками UnityEngine,
демонстрационной картой или обещанием автоматического роста FPS.
