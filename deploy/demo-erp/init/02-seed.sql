-- Данные демо-ERP. Даты считаются от now(): «за прошлую неделю» данные есть всегда.
-- setseed делает «случайные» данные одинаковыми при каждом пересоздании базы.
select setseed(0.42);

insert into employees (full_name, position, hired_at) values
    ('Анна Смирнова',     'Руководитель отдела продаж', '2019-03-11'),
    ('Игорь Петров',      'Старший менеджер',           '2020-06-01'),
    ('Мария Кузнецова',   'Менеджер по работе с клиентами', '2021-02-15'),
    ('Дмитрий Волков',    'Менеджер по работе с клиентами', '2021-09-20'),
    ('Елена Соколова',    'Менеджер по работе с клиентами', '2022-04-04'),
    ('Алексей Морозов',   'Менеджер по работе с клиентами', '2023-01-16'),
    ('Ольга Новикова',    'Менеджер по работе с клиентами', '2023-08-28'),
    ('Сергей Лебедев',    'Младший менеджер',           '2024-11-05');

-- 12 корней × 5 окончаний = 60 уникальных компаний.
insert into customers (name, inn, city, segment, manager_id, created_at)
select format('%s «%s%s»', case when n % 7 = 0 then 'АО' when n % 5 = 0 then 'ИП' else 'ООО' end, root, suffix),
       lpad((7700000000 + n * 104729)::text, 10, '0'),
       (array['Москва', 'Санкт-Петербург', 'Екатеринбург', 'Казань', 'Новосибирск', 'Нижний Новгород', 'Самара'])[1 + n % 7],
       case when n % 10 = 0 then 'Дистрибьютор' when n % 3 = 0 then 'Опт' else 'Розница' end,
       1 + n % 8,
       current_date - (200 + n * 13)::int
from (
    select row_number() over () as n, root, suffix
    from unnest(array['Север', 'Вектор', 'Альфа', 'Гранит', 'Меридиан', 'Орион',
                      'Прогресс', 'Сфера', 'Восход', 'Импульс', 'Кристалл', 'Атлант']) as root
    cross join unnest(array['Трейд', 'Снаб', 'Групп', 'Сервис', 'Логистик']) as suffix
) names;

insert into product_categories (name) values
    ('Ноутбуки'), ('Мониторы'), ('Периферия'), ('Сетевое оборудование'), ('Комплектующие'), ('Оргтехника');

insert into products (sku, name, category_id, price, cost)
select sku, name, (select id from product_categories c where c.name = category), price, round(price * 0.72, 2)
from (values
    ('NB-1401', 'Ноутбук Lenovo ThinkPad E14',        'Ноутбуки', 78900),
    ('NB-1402', 'Ноутбук HP ProBook 450 G10',         'Ноутбуки', 84500),
    ('NB-1403', 'Ноутбук ASUS ExpertBook B1',         'Ноутбуки', 62300),
    ('NB-1404', 'Ноутбук Huawei MateBook D16',        'Ноутбуки', 71200),
    ('NB-1405', 'Ноутбук Dell Latitude 5540',         'Ноутбуки', 98700),
    ('MN-2401', 'Монитор Samsung 24" S24C310',        'Мониторы', 11900),
    ('MN-2402', 'Монитор LG 27" 27MP400',             'Мониторы', 15400),
    ('MN-2403', 'Монитор Dell 27" P2723D',            'Мониторы', 32800),
    ('MN-2404', 'Монитор AOC 23.8" 24B2XH',           'Мониторы', 9800),
    ('MN-2405', 'Монитор Philips 34" 346E2CUAE',      'Мониторы', 36500),
    ('PR-3401', 'Клавиатура Logitech K120',           'Периферия', 990),
    ('PR-3402', 'Мышь Logitech M185',                 'Периферия', 1190),
    ('PR-3403', 'Комплект Logitech MK270',            'Периферия', 2490),
    ('PR-3404', 'Гарнитура Jabra Evolve2 30',         'Периферия', 7900),
    ('PR-3405', 'Веб-камера Logitech C920',           'Периферия', 8400),
    ('PR-3406', 'Док-станция Dell WD19S',             'Периферия', 21500),
    ('NW-4401', 'Коммутатор TP-Link TL-SG1024D',      'Сетевое оборудование', 8900),
    ('NW-4402', 'Маршрутизатор MikroTik hAP ax2',     'Сетевое оборудование', 10700),
    ('NW-4403', 'Точка доступа Ubiquiti U6 Lite',     'Сетевое оборудование', 14200),
    ('NW-4404', 'Коммутатор Cisco CBS250-24T',        'Сетевое оборудование', 38900),
    ('NW-4405', 'Патч-корд Cat6 2 м',                 'Сетевое оборудование', 190),
    ('CP-5401', 'SSD Samsung 980 1 ТБ',               'Комплектующие', 8900),
    ('CP-5402', 'SSD Kingston A400 480 ГБ',           'Комплектующие', 3200),
    ('CP-5403', 'Память Kingston 16 ГБ DDR4',         'Комплектующие', 3900),
    ('CP-5404', 'Память Crucial 32 ГБ DDR5',          'Комплектующие', 9600),
    ('CP-5405', 'Блок питания be quiet! 650W',        'Комплектующие', 8200),
    ('OF-6401', 'МФУ HP LaserJet M236sdw',            'Оргтехника', 27900),
    ('OF-6402', 'Принтер Pantum P2500W',              'Оргтехника', 9900),
    ('OF-6403', 'Картридж HP 136A',                   'Оргтехника', 4900),
    ('OF-6404', 'ИБП APC Back-UPS 650',               'Оргтехника', 9300),
    ('OF-6405', 'Шредер Fellowes 8Cd',                'Оргтехника', 11800)
) as p(sku, name, category, price);

insert into warehouses (name, city) values
    ('Центральный', 'Москва'),
    ('Северо-Запад', 'Санкт-Петербург'),
    ('Урал',         'Екатеринбург');

-- Около 12% позиций — ниже минимального остатка: есть что спросить «что пора дозаказать».
insert into stock (warehouse_id, product_id, quantity, reorder_level)
select w.id, p.id,
       case when random() < 0.12 then floor(random() * 10)::int else 25 + floor(random() * 220)::int end,
       case when p.price > 30000 then 10 else 20 end
from warehouses w cross join products p;

-- 1800 заказов за полгода, чаще — ближе к сегодняшнему дню и от крупных клиентов.
insert into orders (number, customer_id, manager_id, status, ordered_at, shipped_at)
select format('ЗК-%s', lpad(row_number() over (order by g.ordered_at)::text, 6, '0')),
       c.id,
       c.manager_id,
       g.status,
       g.ordered_at,
       case when g.status = 'shipped' then g.ordered_at + (1 + floor(random() * 4)) * interval '1 day' end
from (
    select date_trunc('minute', now() - power(random(), 1.4) * interval '180 days'
                                      + (9 + floor(random() * 9)) * interval '1 hour'
                                      - extract(hour from now()) * interval '1 hour') as ordered_at,
           1 + floor(power(random(), 2.2) * 60)::int as customer_rank,
           random() as roll
    from generate_series(1, 1800)
) g0
cross join lateral (
    select g0.ordered_at,
           case
               when g0.ordered_at > now() - interval '2 days' then (case when g0.roll < 0.5 then 'new' else 'paid' end)
               when g0.roll < 0.05 then 'cancelled'
               else 'shipped'
           end as status
) g
join customers c on c.id = g0.customer_rank
where g.ordered_at <= now();

-- 1–4 разных товара в заказе; оптовики берут больше штук.
insert into order_items (order_id, product_id, quantity, price)
select o.id, p.id,
       case when c.segment = 'Розница' then 1 + floor(random() * 5)::int else 5 + floor(random() * 40)::int end,
       p.price
from orders o
join customers c on c.id = o.customer_id
cross join lateral (
    select id, price from products
    order by random()
    limit 1 + (o.id * 7) % 4   -- зависит от o.id, поэтому выполняется для каждого заказа
) p;

insert into payments (order_id, amount, method, paid_at)
select t.order_id, t.total,
       (array['Безналичный', 'Безналичный', 'Безналичный', 'Карта', 'Наличные'])[1 + floor(random() * 5)::int],
       least(t.ordered_at + random() * interval '36 hours', now())
from order_totals t
where t.status in ('paid', 'shipped');
