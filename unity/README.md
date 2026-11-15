# White Guard — Unity

Здесь находится Unity-проект полной top-down экранизации «Белой гвардии»
Александра Бушкова: девять последовательных карт, бой, сопровождение,
расследование, спасение людей и финал в советском посольстве. Повествование
построено вокруг мыслей Мазура и рисованных сцен. Общий боевой пакет взят
из переноса третьего уровня Acorn Hunter.

**Новая кампания реализована в коде и данных, но ещё не запущена в Unity.**
Это рабочий проект для нативной проверки, а не готовый релиз. Основа переноса
и исходное вступление ранее проверены на ПК в коммите `ec56f02`; эти результаты
не подтверждают работу добавленных девяти карт.

Подробный текущий отчёт: [WHITE_GUARD_PRODUCTION_STATUS.md](docs/WHITE_GUARD_PRODUCTION_STATUS.md).
Сюжет, карты и ограничения адаптации: [WHITE_GUARD_CAMPAIGN.md](docs/WHITE_GUARD_CAMPAIGN.md).
Предыдущие [хэндофф переноса](HANDOFF_UNITY_MIGRATION.md) и
[промпт продолжения](NEXT_CODEX_PROMPT.txt) относятся к истории общего
переноса Acorn/Unity. Для новой игры порядок нативной проверки приведён
в текущем production-status. Прежние ограничения «только вступление»
и нехватка Cloud-диска не описывают нынешний этап.

## Открыть игру

Нужен **Unity 6.3 LTS 6000.3.28f1**, revision `8f082d2fe476`, с действующей
лицензией. Добавить существующий каталог `unity/WhiteGuard` в Unity Hub,
дождаться импорта и открыть **`Assets/Scenes/WhiteGuard.unity`**. Нажать Play.
Это основная сцена с меню и кампанией. `Assets/Scenes/Opening.unity` —
сохранённый отдельный проигрыватель исходного вступления.

Карты создаются из авторских данных при запуске; отдельных `.unity`-сцен
на каждую главу нет. Общий пакет подключён локально из
`unity/Packages/com.arkon.runtime`. Ввод использует legacy Input.

В текущей Cloud-среде полный Editor уже установлен: прежний недостаток места
устранён. Настоящий запуск Editor остановился **до импорта проекта**, exit
**198**, `No valid Unity Editor license found`. Новая кампания здесь не
импортирована, не исполнялась в PlayMode и не собрана в Player. Подробности
записаны в [native-setup-status.json](docs/native-setup-status.json).

## Кампания и управление

Маршрут: «Кортеж» → «Приказ и договор» → «Букет» → «Слишком тихо» →
«Лагерь геологов» → «Дом антиквара» → «Гобсанто» → «Жёлтый автобус» →
«За воротами». Дворец, церемония и эпилог проходят без стрельбы. Кортеж
и автобус используют тот же top-down контроллер с сопровождением транспорта.

| Ввод | Действие |
|---|---|
| WASD / мышь | Движение / прицел |
| ЛКМ / левый Shift | Огонь / бег |
| E | Действие по контексту: осмотр, дверь, спасение, переноска |
| Q / 1, 2, 3 | Бросок бутылки / пистолет, дробовик, бита |
| J / Esc | Журнал / пауза |
| Space или Enter | Следующая карточка повествования |
| R после поражения | Повтор с контрольной точки |
| F8 | Отладочный выбор всех эпизодов; также доступен в главном меню |

Сохранение: `Application.persistentDataPath/white-guard-campaign-v1.json`.
Контрольные точки создаются при входе на карту, отмеченных действиях и выходе
в меню из игры. Продолжение восстанавливает состояние карты; файл другой
версии кампании отклоняется. Старые Godot-сохранения автоматически не читаются.

## Содержимое

- [WhiteGuard](WhiteGuard) — игровой цикл, интерфейс, камера, звук, диагностика
  и две входные сцены.
- [WhiteGuardCampaign](WhiteGuard/Assets/Resources/WhiteGuardCampaign) —
  9 карт, 71 задача, 74 противника, 30 взаимодействий, 88 сюжетных карточек
  и 88 новых MP3. Дополнительный монтаж содержит 132 дискретных плана.
- [WhiteGuardArt](WhiteGuard/Assets/Resources/WhiteGuardArt) — 12 новых
  PNG-атласов, 182 именованных региона: персонажи, предметы, окружение и
  иллюстрации. Нативная художественная приёмка ещё требуется.
- [com.arkon.runtime](Packages/com.arkon.runtime) — независимые C#-модели
  и реальные Unity-адаптеры движения, боя, AI, дверей, предметов и вступления.
- [Tests](Tests) и [Validation](Validation) — переносимые проверки,
  компиляция против официальных Unity DLL и отчёты с указанными границами.

Исходные рисунки вступления, монолог из 1018 слов, 12 MP3 и шрифты сохранены.
Полный текст книги не публикуется. Полный Acorn Hunter остаётся в приватном
`masterofmagic234/Arkon`; его игровые сцены и материалы не переносятся сюда.

## Проверки и сборка

Команды ниже выполнять из корня Kisa. `Unity` означает исполняемый файл
Editor 6000.3.28f1; заменить абсолютные пути на реальные пути своего ПК.
Не открывать один проект одновременно в Hub и batch-процессе.

```bash
python3 unity/Tools/validate_layout.py
python3 unity/Tools/check_core.py --dotnet dotnet
python3 unity/Tools/prepare_campaign.py --check
python3 unity/Tools/validate_campaign_maps.py
python3 unity/Tools/validate_campaign_media.py
python3 unity/Tools/check_campaign.py --dotnet dotnet
```

Эти команды проверяют сохранность исходников, переносимую логику, контракты
задач и статическую геометрию. Они не запускают Unity. Для компиляции всего
нового игрового C# против официальных managed DLL:

```bash
python3 unity/Tools/check_unity_api.py --dotnet dotnet \
  --reference-root /absolute/path/to/verified-managed-reference-root --game
```

В активированном Editor:

```bash
Unity -batchmode -nographics -quit -projectPath /absolute/path/to/Kisa/unity/WhiteGuard \
  -executeMethod WhiteGuard.Editor.WhiteGuardGameBuild.ValidateData -logFile /absolute/path/to/campaign-import.log

Unity -batchmode -nographics -projectPath /absolute/path/to/Kisa/unity/WhiteGuard \
  -executeMethod WhiteGuard.Editor.CampaignGameChecks.Run -logFile /absolute/path/to/campaign-startup.log

Unity -batchmode -nographics -quit -projectPath /absolute/path/to/Kisa/unity/WhiteGuard \
  -executeMethod WhiteGuard.Editor.WhiteGuardGameBuild.BuildLinuxGame -logFile /absolute/path/to/campaign-build.log
```

К **`CampaignGameChecks.Run` не добавлять `-quit`**: команда сама входит
в PlayMode, проверяет загрузку девяти карт, начальную контрольную точку,
паузу, повтор и очистку, затем завершает процесс. Отчёт по умолчанию —
`unity/WhiteGuard/Logs/CampaignMapStartup.json`. Она не проходит игру до конца
и не проверяет изображение или звук.

Для Windows: `WhiteGuard.Editor.WhiteGuardGameBuild.BuildWindowsGame`.
Нужен соответствующий Standalone build module. Выходы по умолчанию:
`unity/WhiteGuard/Builds/Linux/WhiteGuard` и
`unity/WhiteGuard/Builds/Windows/WhiteGuard.exe`. Переменная `WHITEGUARD_BUILD`
задаёт путь к исполняемому файлу, `WHITEGUARD_DEVELOPMENT=1` — Development Build.
Успешная сборка отдельно требует запуска Player и полного ручного прохождения.

Для Linux debug-сборки с меню уровней подготовлен последовательный сборщик:

```bash
python3 unity/Tools/build_linux_debug.py --unity /absolute/path/to/6000.3.28f1/Editor/Unity
```

Он выполняет реальные проверки импорта, native-запуск карт и `BuildLinuxGame`,
затем проверяет executable/Data/UnityPlayer.so и создаёт `.tar.gz` с SHA-256,
управлением и метаданными проверенного исходника. Каждая попытка использует
новый каталог в `unity/WhiteGuard/Builds/LinuxDebug`; старый Player не упаковывается
после ошибки. При нынешнем exit 198 сборщик останавливается до сборки и архива.
Debug-выбор уровня использует отдельное сохранение. Архив считается
экспериментальным и не заменяет полного прохождения и визуальной приёмки.
