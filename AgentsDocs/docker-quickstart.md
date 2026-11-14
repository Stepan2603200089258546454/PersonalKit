# Docker: быстрый запуск

Короткая инструкция для первого запуска. Подробности, объяснения и решение проблем —
в [docker.md](docker.md).

## Что получится

- `DevHome.Identity` — вход и админка: `http://<IP-компьютера>:5201/`
- `DevHome` — веб-приложение: `http://<IP-компьютера>:5149/`
- `DevHome.Api` — шаблон API, доступен только внутри Docker

## Шаги

1. Запустите Docker Desktop и дождитесь статуса **Engine running**.
2. Убедитесь, что контейнер с PostgreSQL запущен (обычно `postgres_db`).
3. Узнайте IPv4-адрес компьютера в локальной сети:

   ```powershell
   ipconfig
   ```

   Нужен адрес вида `192.168.1.50` из раздела вашего сетевого адаптера.
   Не берите `127.0.0.1`, адреса `vEthernet` и `172.17.x.x`.

4. Запустите сборку и старт одной командой из корня решения:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File AgentsTools/Start-Docker.ps1 -Version 0.1.0 -PublicHost 192.168.1.50
   ```

   Подставьте свой адрес. Скрипт сам создаст `.env`, соберёт образы и запустит контейнеры.

   Можно не открывать PowerShell: дважды щёлкните по `Build-Docker.cmd` рядом
   с `docker-compose.yml`, выберите режим (1 — из исходников, 2 — из `publish/`),
   проверьте версию и адрес. Окно останется открытым, чтобы было видно результат.

5. Откройте в браузере:

   - `http://192.168.1.50:5201/` — Identity;
   - `http://192.168.1.50:5149/` — DevHome.

   С телефона в той же Wi-Fi-сети откройте
   `http://192.168.1.50:5201/health`: должен вернуться JSON с `"status":"ok"`.
   Если не открывается, смотрите раздел 8.1 в [docker.md](docker.md) —
   чаще всего мешают гостевая сеть, изоляция клиентов на роутере или VPN на ПК.

6. Войдите. Если база уже была, используйте существующую учётную запись.
   Если база чистая, скрипт создаст администратора из `.env`:
   почта `ADMIN_EMAIL`, пароль `ADMIN_PASSWORD`.
   Сразу смените пароль в личном кабинете.

## Если что-то не так

```powershell
docker compose ps
docker compose logs -f identity web api
```

Порт занят — закройте запущенное из Visual Studio приложение или поменяйте
`IDENTITY_PORT`/`WEB_PORT` в `.env`.

`network postgresql_backend not found` — контейнер PostgreSQL не в этой сети.
Подключите его:

```powershell
docker network connect postgresql_backend postgres_db
```

Остановить приложения (база не удаляется):

```powershell
docker compose down
```

Остановить и удалить ключи Identity (пользователи выйдут из приложений):

```powershell
docker compose down -v
```
