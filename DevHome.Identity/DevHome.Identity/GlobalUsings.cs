// Пространство имён DevHome.Identity.OpenIddict перекрывает корневое OpenIddict,
// поэтому ссылки на пакет подключаются абсолютно и глобально: так Razor-компоненты
// не зависят от порядка @using и не «теряют» типы OpenIddict.
global using global::OpenIddict.Abstractions;
global using global::OpenIddict.EntityFrameworkCore.Models;
global using static global::OpenIddict.Abstractions.OpenIddictConstants;
