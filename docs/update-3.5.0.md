# Обновление до RoAura 3.5.0 при ошибке HTTP 504

Полностью закройте RoAura 3.4.0. Если встроенное обновление не загружает установщик, откройте PowerShell и выполните этот блок. Команда скачивает официальный Setup через API GitHub и проверяет его контрольную сумму.

```powershell
$setup = Join-Path $env:TEMP 'RoAura-3.5.0-Setup.exe'
$sha256 = '388b5eb534fe52050a49113924baebccc5326d954fd1fa89e158f6f4bf717938'
curl.exe --fail --location --retry 2 --header 'Accept: application/octet-stream' --output $setup 'https://api.github.com/repos/alerion08/RoAura/releases/assets/723526764'
if ($LASTEXITCODE -ne 0) { throw 'Не удалось скачать установщик RoAura.' }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash -ne $sha256) {
    throw 'Контрольная сумма не совпала. Не запускайте этот файл.'
}
Start-Process -FilePath $setup
```

Завершите установку поверх текущей версии. Настройки и пресеты RoAura сохраняются. Удалять старую версию перед установкой не требуется.

[Релиз 3.5.0 и опубликованные контрольные суммы](https://github.com/alerion08/RoAura/releases/tag/v3.5.0)
