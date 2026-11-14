# Unity migration — Acorn Hunter и White Guard

**Перенос начат. Готовых Unity-версий обеих игр пока нет.** Godot-проекты,
релизные сборки и одобренная рисовка сохранены. Здесь находится общий C#-пакет,
кандидат Unity-проигрывателя вступления и проверки переносимой логики.

## Структура

- `Packages/com.arkon.runtime` — общий пакет: независимые модели и настоящие
  Unity-адаптеры. Это не новая рисовка и не генератор картинок.
- `WhiteGuard` — Unity-проект с `Assets/Scenes/Opening.unity`. Исходные PNG,
  12 MP3, монолог, разметка и шрифты скопированы без изменения содержимого.
- `Tests/Core` и `Tests/Parity` — компиляция C# 9 / .NET Standard 2.1 и исполнение
  проверок на .NET 10, включая сравнение с настоящими Godot-компонентами.
- `Tests/UnityApi` — отдельная проверка типов через подлинные managed DLL Unity.
  Unity DLL сюда не включаются и не публикуются.
- `Validation` — результаты с явными границами проверки.

Полный Acorn Hunter остаётся в **приватном** `masterofmagic234/Arkon`, ветка
`feat/unity-migration`, каталог `unity/AcornHunter`. Сюжетные карты, персонажи,
модели, видео и инвентаризация этой игры не копируются в публичный Kisa.
Его восемь игровых маршрутов пока имеют статус `not_ported`.

## Что перенесено в код

Здоровье/неуязвимость, оружие и раздельные резервы, входные action/throw edges,
движение Level 3, AI-решения и room-duty, A* и навигационная сетка, двери,
оглушение, предметы, ограниченный пул, swept-projectile policy и выбор целей.
Для гонки — исходная трансмиссия, reverse interlock, сплайн/прогресс,
проекция/clipping и контактная математика. Полный контроллер и renderer гонки
ещё не портированы; WheelCollider их не подменяет.

Написаны Unity-адаптеры 2D query/body/input/projectile, хранения прогресса и
PNG/MP3-вступления. Компиляция адаптера и работа в настоящем Unity — разные
проверки. Цикл сцены, импорт, звук, физика и платформенная сборка требуют Editor.
Нативный формат Unity-progress поддерживает примитивные факты; автоматического
чтения прежнего Godot ConfigFile и произвольных вложенных Variant пока нет.

## Открыть кандидата White Guard

Версия: **Unity 6.3 LTS 6000.3.28f1**, revision `8f082d2fe476`.
Добавить `unity/WhiteGuard` в активированный Unity Hub, открыть
`Assets/Scenes/Opening.unity` и нажать Play. Автоматически создаются камера,
AudioListener и проигрыватель. Ввод использует legacy Input, настройка0.

Это перенос текущего вступления на другой движок. Его прежние 67 планов и
недостатки режиссуры сохранены; режиссура не считается исправленной или
одобренной. Изображения и голос не перерисовываются и не переозвучиваются.

Для настоящего Editor предусмотрены команды:

```bash
Unity -batchmode -nographics -quit -projectPath /path/to/Kisa/unity/WhiteGuard \
  -executeMethod Arkon.Unity.Editor.MigrationChecks.VerifyIntroImports -logFile intro-import.log
Unity -batchmode -nographics -quit -projectPath /path/to/Kisa/unity/WhiteGuard \
  -executeMethod Arkon.Unity.Editor.MigrationChecks.VerifyNativePhysics -logFile native-physics.log
```

Эти команды подготовлены, но в текущей Cloud-среде **не выполнялись**.
Linux development build: `Arkon.Unity.Editor.MigrationChecks.BuildLinuxIntro`.

## Проверить доступное без Editor

```bash
python3 unity/Tools/validate_layout.py
python3 unity/Tools/check_core.py --dotnet /path/to/dotnet
```

Тесты используют снимок исполнения исходных Godot-компонентов и сверяют хеши
GDScript/TRES. Для свежего снимка после импорта Godot-проекта:

```bash
godot --headless --editor --import --quit --path white_guard
godot --headless --path white_guard --script /absolute/path/to/Kisa/unity/Tools/export_combat_parity.gd -- /tmp/combat-oracle.json
python3 unity/Tools/check_core.py --dotnet /path/to/dotnet --oracle /tmp/combat-oracle.json
```

API-компиляция против DLL из официального дистрибутива 6000.3.28f1:

```bash
dotnet build unity/Tests/UnityApi/Arkon.UnityApi.csproj \
  -p:UnityReferenceRoot=/path/to/verified-managed-reference-root
```

Это не запуск Unity, не native physics test и не измерение FPS.

## Текущий блокер

На выбранной машине нет активированного Editor. Официальный Linux Editor
требует около 8,14 GiB после установки; перед работой было свободно 7,32 GiB,
а проекты, Library и платформенные модули требуют дополнительного места.
Нужна более вместительная среда либо ПК с активированным Unity.

Референсы для проверки версии, размера и активации:
[Unity 6.3 LTS](https://unity.com/releases/unity-6/support),
[дистрибутив и размеры](https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.3.28f1),
[активация](https://docs.unity.com/en-us/engine/6000.3/manual/get-started/install-and-upgrade/licenses-and-activation/license-activation-methods).

Дальше: реальный импорт/PlayMode, сверка стен/дверей/AI/декодерного таймлайна,
затем уровневые сцены и оркестрация всех трёх режимов Acorn, его шейдеры,
медиа и UI. Полный порт завершён только после прохождения этих этапов.
