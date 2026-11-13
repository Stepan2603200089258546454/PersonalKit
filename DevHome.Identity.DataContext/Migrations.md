Библиотека для работы с базой данных.

Для создания миграции в окне Package Manager Console (Проект DataContext) вводятся следующие команды:

Add-Migration название_миграции Update-Database

add-migration <название_миграции> -context DBContext -project DataContext

Название миграции представляет произвольное название, главное чтобы все миграции в проекте имели разные названия.

Удалить созданную миграцию:
Remove-Migration