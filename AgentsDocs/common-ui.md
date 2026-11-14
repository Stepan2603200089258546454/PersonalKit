# Common.UI — библиотека компонентов PersonalKit

Перенесена из старого проекта FamilyChat (`Common.UI`) и переработана под .NET 11.
Проект: `Common.UI/Common.UI.csproj` (Razor Class Library, `net11.0`, работает и в Blazor Server, и в Blazor WebAssembly).

## Требования приложения

- Bootstrap 5.3 (классы `bg-body-secondary`, `text-body-secondary`, `offcanvas`, `nav-pills`);
- `builder.Services.AddCascadingAuthenticationState()` — меню и верхняя панель проверяют вход пользователя;
- `MapRazorComponents` с нужными режимами отрисовки; для страниц отдельной сборки — `AdditionalAssemblies` в `Router`.

## Что входит

| Компонент | Назначение |
|---|---|
| `NavigationMenu` | Каркас страницы: боковая панель, верхняя панель, область контента. На десктопе панель постоянная и сворачивается до иконок, на мобильном открывается offcanvas Bootstrap. |
| `ThemeSwitcher` | Переключатель светлой, тёмной и системной темы Bootstrap. |
| `CommonUiAssets` | Единая точка подключения стилей и скриптов библиотеки (см. раздел «Подключение ресурсов»). |
| `[MenuItem]` | Атрибут страницы: название, описание, раздел, иконка, порядок, роли. |

Не перенесены `PageContainer` и панели `PanelBase`/`PanelChild`/`PanelChildPush`: в старом проекте это был демонстрационный каркас чата с заглушками, то есть незаконченный компонент.

## Зависимости приложения

Библиотека не заменяет Bootstrap, а использует его классы. В `App.razor` приложения нужны Bootstrap 5.3 и Bootstrap Icons:

```razor
<link rel="stylesheet" href="@Assets["lib/bootstrap/dist/css/bootstrap.min.css"]" />
<link rel="stylesheet" href="@Assets["lib/bootstrap-icons/font/bootstrap-icons.min.css"]" />
<script src="@Assets["lib/bootstrap/dist/js/bootstrap.bundle.min.js"]"></script>
```

Стили и скрипты самой библиотеки приложение не подключает: их подключают компоненты (см. ниже).

## Подключение ресурсов

Blazor хранит только один блок `HeadContent` на страницу: если два компонента выводят `HeadContent`, в `head` попадает содержимое последнего отрисованного. Поэтому каждый компонент библиотеки объявляет `<CommonUiAssets />`, а этот компонент выводит полный набор файлов библиотеки. Набор одинаковый, поэтому результат не зависит от того, какой компонент отрисовался последним, а приложению не нужна отдельная настройка.

Если компонент библиотеки используется на странице без `NavigationMenu`, `CommonUiAssets` всё равно подключит нужные файлы — достаточно самого факта использования компонента.

## Регистрация в DI

```csharp
builder.Services.AddCommonUi(options =>
{
    options.ApplicationName = "DevHome";
    options.ApplicationIcon = "bi bi-house-gear-fill";
    options.HomeUrl = "";                       // адрес главной, на него ведёт логотип
    options.LoginUrl = "login";                 // пусто — кнопки «Войти» нет
    options.RegisterUrl = "Account/Register";   // пусто — кнопки «Регистрация» нет
    options.ProfileUrl = "Account/Manage";      // пусто — пункта «Профиль» нет
    options.LogoutUrl = "Account/Logout";       // пусто — кнопки «Выход» нет
    options.LogoutUsePost = true;               // false — выход обычной ссылкой
    options.ShowSearch = false;
    options.ShowThemeSwitcher = true;

    // Сборки, в которых ищутся страницы с атрибутом [MenuItem].
    options.AddAssembly(typeof(Program).Assembly);
    options.AddAssembly(typeof(SomeClientAssembly._Imports).Assembly);
});
```

`AddCommonUi` регистрирует настройки и `IMenuCatalog` — справочник страниц, собранный из атрибутов при первом обращении.

## Разметка страницы

```razor
@inherits LayoutComponentBase
@using Common.UI.Components.Layout

<NavigationMenu>
    @Body
</NavigationMenu>
```

## Описание страницы атрибутом

Атрибут ставится прямо на страницу, отдельный builder меню не нужен:

```razor
@page "/weather"
@attribute [MenuItem("Погода", Description = "Пример потоковой отрисовки", Section = "Разделы", Icon = "bi bi-cloud-sun", Order = 30)]
```

| Свойство | Назначение |
|---|---|
| `Title` (позиционный) | Название пункта меню и заголовок в верхней панели. |
| `Description` | Описание в верхней панели (скрыто на узких экранах). |
| `Section` | Раздел (блок) меню: пункты с одинаковым разделом группируются, раздел выводится заголовком. |
| `Icon` | Классы Bootstrap Icons, например `bi bi-gear`. |
| `Url` | Адрес пункта. Не задан — берётся маршрут страницы из `@page`. Нужен для внешних ссылок. |
| `Roles` | Роли через запятую: пункт виден, если у пользователя есть любая из них. |
| `RequiresAuthorization` | Пункт виден только вошедшим пользователям. |
| `OpenInNewTab` | Открывать в новой вкладке. |
| `Order` | Порядок внутри раздела (меньше — выше). Задаёт и порядок разделов. |
| `Hidden` | Не показывать в меню, маршрут при этом работает. |

Правила:

- раздел без видимых пунктов не выводится, поэтому заголовок «Доступ» не появится у анонимного пользователя;
- для страниц из отдельной сборки (например, Blazor WebAssembly-клиента) эта сборка должна быть добавлена в `AddAssembly` и подключена в `Routes` как `AdditionalAssemblies`;
- страницы без атрибута в меню не попадают, но работают по своему маршруту.

## Поведение меню

- **Десктоп (≥ 992 px):** панель шириной 264 px, кнопка внизу сворачивает её до 76 px (только иконки). Состояние хранится в `localStorage` под ключом `cui.sidebarCollapsed`.
- **Мобильный (< 992 px):** панель скрыта, в верхней панели появляется кнопка-гамбургер, меню открывается offcanvas Bootstrap. После выбора пункта offcanvas закрывается.
- **Разделы меню сворачиваются:** заголовок раздела — кнопка с шевроном; состояние каждого раздела хранится в `localStorage` под ключом `cui.section.v2.<название>`. Обе копии меню (панель и offcanvas) синхронизируются между собой. В свёрнутой до иконок панели разделы всегда раскрыты, иначе часть иконок пропала бы.
- **Подсказки в свёрнутой панели:** когда остаются только иконки, при наведении на пункт показывается Bootstrap-подсказка справа с названием страницы (Bootstrap Tooltip, `container: body`, поэтому её не обрезает прокрутка панели). В развёрнутой панели подсказки отключены. У ссылок есть `aria-label`, поэтому пункты остаются доступными для скринридера и без текста.
- Свёрнутость панели, состояние разделов и подсказки восстанавливаются после усиленной навигации: Blazor подставляет серверную разметку, а наблюдатель за DOM возвращает сохранённое состояние.
- Текущая страница выделяется классом `active`; активность определяется по шаблону маршрута, включая параметры (`weather/{city}`). Оформление активного пункта переопределяет `nav-pills`: вместо яркой заливки основным цветом — вторичный фон и рамка при тех же скруглениях, текст остаётся цветом тела.
- Bнешний вид построен на Bootstrap: `nav nav-pills`, `collapse`, `dropdown`, `offcanvas`, `card`, `table`. Собственные стили — только размеры панели, каркаса, заголовка раздела и нижней кнопки (`wwwroot/NavigationMenu.css`, ~220 строк).

## Тема

- Выбранное значение (`light`, `dark`, `auto`) хранится в `localStorage` под ключом `cui.theme`.
- Рядом пишется cookie `cui.theme` с **итоговой** темой (`light` или `dark`). `App.razor` читает её через `HttpContext` и отдаёт в `data-bs-theme` то же значение — иначе усиленная навигация Blazor, в том числе переход на интерактивные (WASM) страницы, возвращала бы светлую тему из серверного HTML.
- Ранний скрипт в `CommonUiAssets` выставляет `data-bs-theme` до отрисовки страницы, поэтому вспышки светлой темы нет.
- Скрипт повторно применяет тему на `enhancedload`/`popstate` и следит за атрибутом `data-bs-theme`: если разметку изменил фреймворк, тема возвращается.
- Подпись и иконка переключателя тоже восстанавливаются после навигации: при усиленной навигации Blazor подставляет серверную разметку верхней панели, а наблюдатель за DOM возвращает выбранное значение («Светлая», «Тёмная», «Системная») и активный пункт меню.
- Для гибридных приложений доступен `window.cuiTheme.set('dark')`.

Пример разметки в `App.razor` приложения:

```razor
<html lang="ru" data-bs-theme="@Theme">

@code {
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private string Theme => HttpContext?.Request.Cookies["cui.theme"] switch
    {
        "dark" => "dark",
        _ => "light"
    };
}
```

## Файлы

```
Common.UI/
├── Common.UI.csproj
├── CommonUiServiceCollectionExtensions.cs   AddCommonUi
├── Menu/
│   ├── MenuItemAttribute.cs                 атрибут страницы
│   ├── MenuPage.cs, MenuSection.cs          модель меню
│   ├── MenuRoutes.cs                        нормализация адресов и шаблоны маршрутов
│   ├── NavigationMenuOptions.cs             настройки приложения
│   ├── IMenuCatalog.cs, MenuCatalog.cs      поиск страниц по сборкам
├── Components/Layout/
│   ├── NavigationMenu.razor                 каркас страницы
│   ├── NavMenuSidebar.razor                 постоянная панель + offcanvas
│   ├── NavMenuList.razor                    разделы и пункты, проверка видимости
│   ├── NavMenuItem.razor, NavMenuItemLink.razor
│   ├── NavMenuTopbar.razor                  заголовок, поиск, тема, пользователь
│   ├── ThemeSwitcher.razor
│   └── CommonUiAssets.razor                 единый HeadContent
└── wwwroot/
    ├── App.css                              базовые стили и скроллбар
    ├── NavigationMenu.css                   каркас и адаптивность
    ├── NavigationMenu.js                    сворачивание панели и разделов, закрытие offcanvas
    └── theme-switcher.js                    переключение и сохранение темы
```
