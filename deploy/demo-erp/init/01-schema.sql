-- Схема демо-ERP. Комментарии к таблицам и колонкам видны LLM через MCP —
-- по ним модель понимает, какие данные где лежат.

create table employees (
    id          int generated always as identity primary key,
    full_name   text not null,
    position    text not null,
    hired_at    date not null
);
comment on table employees is 'Сотрудники отдела продаж (менеджеры клиентов)';

create table customers (
    id          int generated always as identity primary key,
    name        text not null unique,
    inn         char(10) not null unique,
    city        text not null,
    segment     text not null check (segment in ('Розница', 'Опт', 'Дистрибьютор')),
    manager_id  int not null references employees(id),
    created_at  date not null
);
comment on table customers is 'Клиенты-юрлица';
comment on column customers.inn is 'ИНН организации';
comment on column customers.segment is 'Сегмент клиента: Розница, Опт или Дистрибьютор';
comment on column customers.manager_id is 'Закреплённый менеджер (employees.id)';

create table product_categories (
    id    int generated always as identity primary key,
    name  text not null unique
);
comment on table product_categories is 'Категории товаров';

create table products (
    id           int generated always as identity primary key,
    sku          text not null unique,
    name         text not null,
    category_id  int not null references product_categories(id),
    price        numeric(12, 2) not null check (price > 0),
    cost         numeric(12, 2) not null check (cost > 0)
);
comment on table products is 'Каталог товаров';
comment on column products.sku is 'Артикул';
comment on column products.price is 'Цена продажи за штуку, руб.';
comment on column products.cost is 'Себестоимость за штуку, руб.';

create table warehouses (
    id    int generated always as identity primary key,
    name  text not null unique,
    city  text not null
);
comment on table warehouses is 'Склады';

create table stock (
    warehouse_id   int not null references warehouses(id),
    product_id     int not null references products(id),
    quantity       int not null check (quantity >= 0),
    reorder_level  int not null,
    primary key (warehouse_id, product_id)
);
comment on table stock is 'Остатки товаров на складах';
comment on column stock.quantity is 'Текущий остаток, шт.';
comment on column stock.reorder_level is 'Минимальный остаток: ниже него товар нужно дозаказать';

create table orders (
    id           int generated always as identity primary key,
    number       text not null unique,
    customer_id  int not null references customers(id),
    manager_id   int not null references employees(id),
    status       text not null check (status in ('new', 'paid', 'shipped', 'cancelled')),
    ordered_at   timestamptz not null,
    shipped_at   timestamptz
);
comment on table orders is 'Заказы клиентов';
comment on column orders.status is 'new — новый, paid — оплачен, shipped — отгружен, cancelled — отменён';
comment on column orders.ordered_at is 'Дата и время оформления заказа';
comment on column orders.shipped_at is 'Дата отгрузки (только для shipped)';
create index on orders (ordered_at);
create index on orders (customer_id);

create table order_items (
    order_id    int not null references orders(id) on delete cascade,
    product_id  int not null references products(id),
    quantity    int not null check (quantity > 0),
    price       numeric(12, 2) not null,
    primary key (order_id, product_id)
);
comment on table order_items is 'Строки заказа';
comment on column order_items.price is 'Цена за штуку на момент заказа, руб.';

create table payments (
    id        int generated always as identity primary key,
    order_id  int not null references orders(id),
    amount    numeric(14, 2) not null,
    method    text not null check (method in ('Безналичный', 'Карта', 'Наличные')),
    paid_at   timestamptz not null
);
comment on table payments is 'Оплаты заказов';

create view order_totals as
select o.id as order_id,
       o.number,
       o.customer_id,
       o.status,
       o.ordered_at,
       sum(i.quantity * i.price) as total
from orders o
join order_items i on i.order_id = o.id
group by o.id;
comment on view order_totals is 'Сумма каждого заказа, руб.';
