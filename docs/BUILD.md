# Сборка

## Требования

- Windows;
- PowerShell 5.1 или новее;
- .NET SDK 10;
- законно установленная поддерживаемая версия Sunless Sea: Zubmariner;
- `classdata.tpk` из UABEA для работы `AssetTextPatcher`.

Зависимости `AssetsTools.NET`, `AssetsTools.NET.MonoCecil` и `Mono.Cecil`
восстанавливаются из NuGet.

## Сборка утилит

```powershell
dotnet restore .\SunlessSea.Localization.slnx
dotnet build .\SunlessSea.Localization.slnx -c Release
```

## Основные утилиты

- `DllStringPatcher` изменяет только проверенные литералы managed DLL и умеет
  сравнивать внутренние UI lookup-вызовы.
- `AssetTextPatcher` извлекает и заменяет `m_Text` по точному `PathId`.
- `JsonQualityPatcher` и `JsonTutorialPatcher` вносят проверяемые изменения в
  профильный JSON.
- `BinaryDeltaTool` создаёт сжатые дельты `SSRUDEL1` между исходным и
  локализованным файлом.

Пример создания дельты:

```powershell
dotnet .\src\BinaryDeltaTool\bin\Release\net10.0\BinaryDeltaTool.dll `
  create `
  .\release-input\base\Sunless.Game.dll `
  .\release-input\target\Sunless.Game.dll `
  .\release-input\payload\patches\Sunless.Game.dll.ssdelta
```

## Сборка установщика

Подготовьте каталог `release-input\payload` с дельтами и профильным каталогом
`profile\Russian`, затем выполните:

```powershell
.\scripts\New-ReleaseArchive.ps1 `
  -Version '0.1.3' `
  -PayloadPath .\release-input\payload `
  -ManifestPath .\manifests\installer-v0.1.3.json
```

Скрипт запрещает включать в payload целые DLL, Unity assets и исполняемые файлы,
создаёт `FILES.sha256` и помещает ZIP в `artifacts`.

Перед коммитом и выпуском:

```powershell
.\scripts\Test-PublicTree.ps1
```
