# فولدر `Messaging` من الصفر — لواحد أول مرة يسمع عن Message Broker

> الملف ده بيشرح `src/BuildingBlocks/SmartCity.BuildingBlocks/Messaging/` (4 ملفات).
> مبني على الكود اللي على `main` بتاريخ 2026-10-13.
> مش مفترض إنك عارف أي حاجة عن RabbitMQ. اقرا بالترتيب، كل جزء مبني على اللي قبله.
> بعد ما تخلصه، `MESSAGING-EXPLAINED.md` فيه نفس الموضوع بشكل مختصر كمرجع سريع.

---

## الجزء 1 — المشكلة: ليه محتاجين Message Broker أصلاً؟

عندنا خدمات كتير (Pothole, CostCalculation, WorkOrder, Notification...). لما Pothole تسجل حفرة جديدة، لازم:

- CostCalculation تحسب تكلفة الإصلاح
- WorkOrder تفتح أمر عمل
- Notification تبعت إشعار

### الطريقة البديهية: Pothole تكلم كل واحد بنفسها

```
Pothole ──HTTP──► CostCalculation
        ──HTTP──► WorkOrder
        ──HTTP──► Notification
```

دي فيها 4 مشاكل:

1. **لو Notification واقعة؟** الـcall يفشل. Pothole تعمل إيه؟ تلغي الحفرة؟ تستنى؟ تعيد المحاولة لحد إمتى؟
2. **Pothole لازم تعرف كل اللي مهتمين.** لو بكرة ضفنا خدمة Reports، لازم نفتح كود Pothole ونعدّله.
3. **بطء.** Pothole بتستنى التلاتة يردّوا قبل ما تخلص.
4. **سلسلة أعطال.** خدمة واحدة بطيئة بتبطّأ كل اللي بيكلموها.

### الحل: وسيط في النص

```
Pothole ──"اتسجلت حفرة"──► [ Broker ] ──► CostCalculation
                                      ──► WorkOrder
                                      ──► Notification
```

Pothole بتقول للوسيط "حصل كذا" **وتمشي**. الوسيط هو اللي مسؤول يوصّل الخبر لكل اللي مهتم.

- خدمة واقعة؟ الوسيط بيحتفظ بالرسالة لحد ما ترجع.
- خدمة جديدة؟ بتشترك عند الوسيط، وكود Pothole ميتلمسش.
- Pothole مش بتستنى حد.

**الوسيط ده اسمه Message Broker. والـbroker اللي بنستخدمه اسمه RabbitMQ.**

> دي قاعدة في `CLAUDE.md`: الخدمات بتكلم بعض عن طريق RabbitMQ events **بس**. ممنوع خدمة تنادي خدمة تانية مباشرة.

---

## الجزء 2 — RabbitMQ: الأربع كلمات اللي لازم تعرفهم

RabbitMQ برنامج شغال في Docker (بورت 5672). فكّر فيه كـ**مكتب بريد**.

### 1) Message (الرسالة)

شوية JSON بيوصفوا حاجة **حصلت خلاص**. عندنا بنسميها **event**، واسمها دايماً في الماضي: `PotholeSaved`، `WorkOrderCreated`.

```json
{
  "EventId": "…",
  "CorrelationId": "…",
  "PotholeId": "…",
  "Severity": "High"
}
```

### 2) Queue (الطابور)

**صندوق بريد.** الرسايل بتقف فيه بالدور لحد ما حد ياخدها. الرسالة بتفضل في الطابور حتى لو الخدمة اللي المفروض تقراها واقعة.

### 3) Exchange (مكتب الفرز)

اللي بيبعت **مش بيحط الرسالة في queue مباشرة**. بيرميها في exchange، والـexchange هو اللي بيوزّع **نسخة** على كل queue مشترك عنده. الـexchange نفسه مبيخزّنش حاجة.

### 4) Binding (الاشتراك)

الربط بين exchange و queue. معناه: "أي رسالة تيجي على الـexchange ده، حط نسخة منها في الـqueue ده".

### الصورة كاملة

```
                    Exchange "PotholeSaved"
                             │
        ┌────────────────────┼────────────────────┐   ← كل سهم = binding
        ▼                    ▼                    ▼
   Queue بتاع           Queue بتاع           Queue بتاع
 CostCalculation         WorkOrder          Notification
        │                    │                    │
        ▼                    ▼                    ▼
   الخدمة بتقرا         الخدمة بتقرا         الخدمة بتقرا
```

رسالة واحدة دخلت ← 3 نسخ طلعت، واحدة لكل خدمة.

### كلمتين كمان

- **Publisher**: اللي بيبعت الرسالة. **Consumer**: اللي بيقراها.
- **Ack** (acknowledgement): لما الـconsumer يخلص شغله بيقول لـRabbitMQ "تمام، امسحها". لو الخدمة وقعت قبل ما تقول ack، RabbitMQ **بيبعت الرسالة تاني**.

النقطة الأخيرة دي مهمة جداً وهنرجعلها: **الرسالة ممكن توصل أكتر من مرة.** ده اسمه *at-least-once delivery*.

> **شوفها بعينك:** شغّل `docker compose up -d` وخدمة واحدة، وافتح http://localhost:15672
> (smartcity / smartcity). تاب **Exchanges** وتاب **Queues** هتلاقي فيهم كل اللي فوق.

---

## الجزء 3 — MassTransit: إحنا مش بنكلم RabbitMQ مباشرة

RabbitMQ هو السيرفر. عشان تكلمه من C# محتاج تفتح connection، تعمل exchanges و queues و bindings، تحوّل الـobject لـJSON، تبعت ack، تتعامل مع الأخطاء... كود كتير وممل.

**MassTransit** مكتبة .NET بتعمل كل ده عنك. إنت بتكتب حاجتين بس:

**الإرسال:**

```csharp
await publishEndpoint.Publish(new PotholeSaved { ... });
```

**الاستقبال** — class بتعمل implement لـ`IConsumer<T>`:

```csharp
public class PotholeSavedConsumer : IConsumer<PotholeSaved>
{
    public async Task Consume(ConsumeContext<PotholeSaved> context)
    {
        var msg = context.Message;   // الرسالة اللي وصلت
        // ... شغلك هنا
    }
}
```

MassTransit بيشوف إن الـconsumer ده مهتم بـ`PotholeSaved`، فبيعمل لوحده: exchange للـ`PotholeSaved`، وqueue للـconsumer ده، وbinding بينهم. **إنت مبتكتبش ولا سطر عن queues أو exchanges.**

قواعد MassTransit في التسمية:

- **Exchange واحد لكل نوع event**، اسمه = namespace + `:` + اسم الـclass.
  مثال: `SmartCity.Contracts.Events.Detection:PotholeSaved`
- **Queue واحد لكل consumer.**

> لازم MassTransit **8.x**. الإصدار 9 بقى برخصة تجارية، فممنوع نرقّي (`CLAUDE.md`).

---

## الجزء 4 — طيب فولدر `Messaging` بيعمل إيه؟

عندنا 9 خدمات بتستخدم RabbitMQ. لو كل خدمة كتبت إعدادات MassTransit بنفسها، هيبقى فيه 9 نسخ مختلفة شوية عن بعض، وأخطاء صعب تتلاقى.

فولدر `Messaging` هو **الإعدادات المشتركة مكتوبة مرة واحدة**. كل خدمة بتنادي نفس الـmethods.

| الملف | السؤال اللي بيجاوبه |
|---|---|
| `BusExtensions.cs` | إزاي الخدمة تتوصل بـRabbitMQ؟ |
| `OutboxExtensions.cs` | إزاي نضمن إن مفيش رسالة تضيع أو تتنفذ مرتين؟ |
| `PublishExtensions.cs` | إزاي نبعت رسالة بالطريقة الصح؟ |
| `CorrelationIdLogFilter.cs` | إزاي نتتبع رسالة في اللوجز؟ |

وده شكل استخدامهم في `Program.cs` بتاع Pothole:

```csharp
builder.AddSmartCityBus(ServiceName, x =>          // ← BusExtensions
{
    x.AddConsumer<RawDetectionReceivedConsumer>();  // "أنا عايز أسمع النوع ده"
    x.AddSmartCityOutbox<PotholeDbContext>();       // ← OutboxExtensions
});
```

تلات سطور، والخدمة بقت متوصلة بكل حاجة. نفهم بقى كل ملف.

---

## الجزء 5 — `BusExtensions.cs`: التوصيل بـRabbitMQ

فيه method واحدة: `AddSmartCityBus(serviceName, configure)`. نمشي عليها حتة حتة.

### 5.1 — أسماء الـqueues

```csharp
x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, false));
```

اسم الـqueue = اسم الخدمة + اسم الـconsumer (من غير كلمة `Consumer`) بحروف صغيرة وشرطات (kebab-case).

خدمة `Pothole` + `RawDetectionReceivedConsumer` ← `pothole-raw-detection-received`

**ليه اسم الخدمة في الأول مهم؟** فيه `PotholeSavedConsumer` في CostCalculation وفي WorkOrder وفي Notification. من غير الـprefix التلاتة هيبقوا على **نفس الـqueue**. ولما أكتر من واحد يقروا من نفس الـqueue، كل رسالة بتروح **لواحد بس** منهم (بيتقاسموا الشغل). إحنا عايزين العكس: كل خدمة تاخد نسختها. فلازم كل خدمة يبقى ليها queue باسم مختلف.

> الأسماء الفعلية اتأكد منها من تاب **Queues** في http://localhost:15672.

### 5.2 — Health check

```csharp
x.ConfigureHealthCheckOptions(o =>
{
    o.Name = "rabbitmq";
    o.Tags.Add(HealthCheckExtensions.ReadyTag);
});
```

لو الخدمة مش متوصلة بـRabbitMQ، `/health/ready` بيرجع "مش جاهز".

### 5.3 — إيه اللي يحصل لو الـconsumer فشل؟ (Retry + Error queue)

```csharp
x.AddConfigureEndpointsCallback((context, _, endpoint) =>
{
    endpoint.UseConsumeFilter(typeof(CorrelationIdLogFilter<>), context);
    endpoint.UseMessageRetry(r => r.Intervals(
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
});
```

الكود ده بيتطبق على **كل** consumer في الخدمة. السطر التاني (retry) معناه: لو الـconsumer رمى exception (مثلاً الداتابيز كانت مشغولة لحظة):

```
محاولة 1 ✗ → استنى 1s → محاولة 2 ✗ → استنى 2s → محاولة 3 ✗ → استنى 4s → محاولة 4 ✗
                                                                              │
                                              الرسالة تتنقل لـ queue اسمه <queue>_error
```

- أغلب الأخطاء المؤقتة بتتحل لوحدها في إعادة المحاولة.
- لو فشلت 4 مرات، الرسالة **مش بتتمسح**. بتروح queue جانبي آخره `_error`. ده اسمه **Dead Letter Queue (DLQ)**: مكان الرسايل اللي محتاجة بني آدم يبص عليها. تقدر تفتحها من الـRabbitMQ UI وتشوف الـexception.

**عشان كده متعملش `try/catch` يبلع الـexception جوه consumer.** لو بلعته، MassTransit هيفتكر إن كل حاجة تمام، ولا هيعيد المحاولة ولا هيحط الرسالة في الـ`_error`.

السطر الأول (`CorrelationIdLogFilter`) شرحه في الجزء 8.

**كلمة "Filter":** كود بيتنفذ **حوالين** الـconsumer، زي الـmiddleware في ASP.NET. الرسالة بتعدّي على الـfilters واحد ورا التاني قبل ما توصل للـconsumer بتاعك:

```
الرسالة ► CorrelationIdLogFilter ► Retry ► Outbox/Inbox ► الـConsumer بتاعك
```

الترتيب ده مقصود (والكومنت في الكود بيقول كده): اللوج الأول عشان كل اللي بعده يتسجل بالـCorrelationId، وبعدين الـretry عشان كل إعادة محاولة تبدأ transaction جديدة نضيفة.

### 5.4 — الاتصال نفسه

```csharp
cfg.Host(rabbit["Host"] ?? "localhost", rabbit["VirtualHost"] ?? "/", h =>
{
    h.Username(Required(rabbit, "Username"));
    h.Password(Required(rabbit, "Password"));
});
```

العنوان واليوزر والباسورد جايين من `appsettings.json` (سكشن `RabbitMQ`)، مش مكتوبين في الكود. لو اليوزر أو الباسورد مش موجودين، `Required` بيرمي exception برسالة واضحة والخدمة متقومش.

### 5.5 — شكل الـJSON

```csharp
json.PropertyNamingPolicy = null;                    // PotholeId مش potholeId
json.Converters.Add(new JsonStringEnumConverter());  // "High" مش 2
```

كل الخدمات لازم تتفق على شكل الرسالة، وإلا واحدة تبعت والتانية متفهمش. الشكل المتفق عليه موجود في `src/SmartCity.Contracts/MockMessages/mock-events.json`.

### 5.6 — استبعاد الـbase class

```csharp
cfg.Publish<IntegrationEvent>(p => p.Exclude = true);
```

كل الـevents بتورث من `IntegrationEvent`. MassTransit افتراضياً بيعمل exchange للـbase class كمان، وكل event بيروحله. مش محتاجينه، فبنقفله.

### 5.7 — "اعمل كل حاجة"

```csharp
cfg.ConfigureEndpoints(context);
```

ده السطر اللي بيبص على كل الـconsumers المتسجلين ويعمل الـqueues والـexchanges والـbindings في RabbitMQ.

---

## الجزء 6 — `OutboxExtensions.cs`: أهم ملف، وأصعب فكرة

الملف صغير، بس بيحل مشكلتين خطيرتين. افهم المشكلتين الأول.

### المشكلة الأولى: الداتابيز و RabbitMQ حاجتين منفصلتين

الـconsumer في Pothole بيعمل حاجتين: يحفظ الحفرة في الداتابيز، ويبعت `PotholeSaved`. تخيل لو كانوا كده:

```csharp
await _db.SaveChangesAsync();       // ✓ الحفرة اتحفظت
await context.Publish(saved);       // 💥 الخدمة وقعت هنا
```

الحفرة موجودة في الداتابيز، بس **محدش عرف بيها**. لا تكلفة اتحسبت، ولا أمر عمل اتفتح، ومفيش أي error ظاهر. ولو عكست الترتيب المشكلة بتتعكس: الكل عرف بحفرة مش موجودة.

السبب: مفيش طريقة تخلّي الداتابيز و RabbitMQ يعملوا commit مع بعض.

### الحل: Outbox (صندوق الصادر)

بدل ما نبعت لـRabbitMQ على طول، بنكتب الرسالة في **جدول في نفس الداتابيز** اسمه `outbox_message`. وبما إنه نفس الداتابيز، نقدر نحطه في **نفس الـtransaction**:

```
┌────────── transaction واحدة في pothole_db ──────────┐
│  INSERT INTO potholes        (الحفرة)              │
│  INSERT INTO outbox_message  (رسالة PotholeSaved)  │
└───────────────────── COMMIT ───────────────────────┘
        يا الاتنين يتحفظوا، يا ولا واحد.

            ... وبعدين، في الخلفية كل ثانية ...

   MassTransit يقرا outbox_message ──► يبعت لـRabbitMQ ──► يمسح الصف
```

لو RabbitMQ واقع، الرسالة مستنية في الجدول وهتتبعت لما يرجع. **مفيش رسالة تضيع.**

### المشكلة التانية: الرسالة ممكن توصل مرتين

فاكر الـack من الجزء 2؟ تخيل:

1. Pothole استلمت الرسالة وحفظت الحفرة ✓
2. قبل ما تبعت ack، الخدمة وقعت 💥
3. RabbitMQ مجالوش ack ← بيبعت الرسالة تاني
4. Pothole تحفظ **حفرة تانية** لنفس الصورة ✗

### الحل: Inbox (صندوق الوارد)

جدول اسمه `inbox_state` بيتسجل فيه رقم (`MessageId`) كل رسالة اتعالجت. لما رسالة توصل، MassTransit بيسأل الأول: "الرقم ده عدّى عليا قبل كده؟" لو أيوه، بيتجاهلها من غير ما ينادي الـconsumer.

الـconsumer اللي بيستحمل التكرار ده اسمه **idempotent** (دي قاعدة رقم 6 في `CLAUDE.md`).

### الكود

```csharp
x.AddEntityFrameworkOutbox<TDbContext>(o =>
{
    o.UsePostgres();                          // الداتابيز Postgres
    o.UseBusOutbox();                         // شوف "الحالة 2" تحت
    o.QueryDelay = TimeSpan.FromSeconds(1);   // يبص على جدول الـoutbox كل ثانية
});

x.AddConfigureEndpointsCallback((context, _, endpoint) =>
    endpoint.UseEntityFrameworkOutbox<TDbContext>(context));  // ركّب outbox + inbox على كل consumer
```

والـmethod التانية بتضيف الجداول لداتابيز الخدمة:

```csharp
modelBuilder.AddInboxStateEntity();      // inbox_state     — الرسايل اللي اتعالجت
modelBuilder.AddOutboxMessageEntity();   // outbox_message  — الرسايل اللي مستنية تتبعت
modelBuilder.AddOutboxStateEntity();     // outbox_state    — حالة الإرسال
```

كل `DbContext` بينادي `modelBuilder.AddSmartCityOutboxEntities()` في `OnModelCreating`.

### الـoutbox بيشتغل في حالتين

**الحالة 1 — جوه consumer.** بص على `RawDetectionReceivedConsumer` في Pothole:

```csharp
_db.Potholes.Add(pothole);
await context.PublishFollowUp(saved, context.CancellationToken);  // مراحش RabbitMQ! اتجهّز للـoutbox بس
await _db.SaveChangesAsync(context.CancellationToken);            // هنا الحفرة + الرسالة اتحفظوا مع بعض
```

**الحالة 2 — جوه gRPC handler** (مثلاً اليوزر غيّر حالة work order من الـUI). هنا مفيش رسالة جاية، بس `UseBusOutbox()` بيخلّي `IPublishEndpoint` يكتب في الـoutbox برضه. نفس القاعدة: publish وبعدين `SaveChangesAsync` مرة واحدة.

> **أشهر غلطة:** تنسى `SaveChangesAsync`. الرسالة مبتتكتبش في `outbox_message`، فمبتتبعتش أبداً، ومفيش أي error.

### خدمات من غير outbox

`AiDetection` و `VehicleCommunication` مالهمش داتابيز، فمفيش مكان للجدول. مش بينادوا `AddSmartCityOutbox`، والـpublish عندهم بيروح RabbitMQ مباشرة. مقبول، لأنهم مش بيحفظوا حاجة لازم تتفق مع الرسالة.

---

## الجزء 7 — `PublishExtensions.cs`: الطريقتين الوحيدتين للإرسال

قبل الملف، بص على الـbase class بتاع كل event (`IntegrationEvent`):

```csharp
public Guid EventId { get; init; } = Guid.NewGuid();   // رقم فريد للـevent ده
public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
public Guid CorrelationId { get; init; }               // رقم الصورة اللي بدأت القصة كلها
public int SchemaVersion { get; init; } = 1;
```

### يعني إيه CorrelationId؟

صورة واحدة من عربية بتعمل سلسلة events:

```
FrameReceived → RawDetectionReceived → PotholeSaved → CostEstimateGenerated → WorkOrderCreated
```

كل event بتبعته خدمة مختلفة. عشان نعرف إنهم كلهم تبع **نفس الصورة**، كلهم بيشيلوا نفس الـ`CorrelationId`. كل خدمة بتنسخه من الرسالة اللي جاتلها للرسالة اللي بتبعتها، **من غير ما تغيّره**.

لو خدمة واحدة نسيت، السلسلة تتقطع ومتعرفش تتتبع حاجة. والملف ده موجود عشان محدش ينسى.

### `PublishFollowUp` — بتبعت event *بسبب* event جالك

```csharp
await context.PublishFollowUp(saved, context.CancellationToken);
```

بتعمل 3 حاجات:

1. **بتقارن الـ`CorrelationId`** بتاع الرسالة الجديدة بالرسالة اللي جت. مختلفين؟ exception: `CorrelationId must be copied unchanged`.
2. **بتتأكد إن `SourceDetectionId` مش فاضي** (لو الـevent فيه الخاصية دي). فاضي؟ exception: `SourceDetectionId must be set`.
3. **بتبعت، وبتحط `MessageId = EventId`.**

يعني القاعدة مش مكتوبة في documentation وخلاص، الكود **بيرفض يشتغل** لو كسرتها.

### `PublishEvent` — بتبعت event من غير ما يكون جالك حاجة

```csharp
await publishEndpoint.PublishEvent(frameReceived);
```

بتُستخدم في:

- **أول السلسلة:** VehicleCommunication بتستلم صورة من العربية (عن طريق MQTT، مش RabbitMQ) وتبعت `FrameReceived`.
- **بعد أمر من اليوزر:** WorkOrder بتبعت event لما حد يغيّر حالة أمر العمل من الـUI.

مفيش رسالة قبلها تتقارن بيها، فبتعمل حاجة واحدة بس: `MessageId = EventId`.

### ليه `MessageId = EventId`؟

الـinbox (الجزء 6) بيكشف التكرار بالـ`MessageId`. MassTransit لوحده بيدّي كل عملية إرسال `MessageId` **عشوائي جديد**. فلو نفس الـevent اتبعت مرتين، كل مرة برقم مختلف، والـinbox مش هيعرف إنهم نفس الحاجة.

لما نثبّت الـ`MessageId` على الـ`EventId` (اللي جوه الرسالة ومبيتغيرش)، أي نسخة مكررة بتتكشف.

**الخلاصة: متناديش `Publish` بتاعة MassTransit مباشرة أبداً.** استخدم واحدة من الاتنين دول.

---

## الجزء 8 — `CorrelationIdLogFilter.cs`: تتبّع الرسالة في اللوجز

### المشكلة

9 خدمات بتكتب logs في نفس المكان (Seq). سطر زي:

```
Saved pothole 7f3a… (Pothole, High) in Kadıköy
```

تبع أنهي صورة؟ مش باين. ولو طلبنا من كل مبرمج يكتب الـ`CorrelationId` بإيده في كل سطر log، حد هينسى.

### الحل

Filter (فاكر الجزء 5.3؟) بيشتغل قبل كل consumer:

```csharp
var integrationEvent = context.Message as IntegrationEvent;
var correlationId = integrationEvent?.CorrelationId ?? context.CorrelationId;

using (LogContext.PushProperty("CorrelationId", correlationId))
using (LogContext.PushProperty("EventId", integrationEvent?.EventId ?? context.MessageId))
using (LogContext.PushProperty("MessageType", typeof(T).Name))
{
    await next.Send(context);   // ← الـconsumer بتاعك بيشتغل هنا
}
```

- `LogContext.PushProperty` بيقول لـSerilog: "من دلوقتي، أي سطر log زوّد عليه الخاصية دي".
- `next.Send(context)` بيكمّل لباقي الـfilters وبعدين الـconsumer.
- لما الـ`using` يقفل، الخصايص بتتشال.

فأي log جوه الـconsumer بياخد `CorrelationId` و `EventId` و `MessageType` **أوتوماتيك**.

> ده نفس الـ`.Enrich.FromLogContext()` اللي سألت عنه في إعدادات Serilog: السطر ده هو اللي بيخلّي Serilog يقرا الخصايص اللي الـfilter حطّها. من غيره الـfilter ملوش أي تأثير.

**النتيجة:** في Seq (http://localhost:8081) اكتب `CorrelationId = '<الرقم>'` وهتشوف قصة الصورة كاملة من كل الخدمات بالترتيب.

الـ`<T>` في اسم الـclass معناها إنه بيشتغل مع أي نوع رسالة، و`BusExtensions` بيركّبه على كل consumer. إنت مبتعملش حاجة.

---

## الجزء 9 — نجمّع كل حاجة: رحلة رسالة واحدة

AiDetection لقت حفرة في صورة. إيه اللي بيحصل؟

```
① AiDetection تبعت RawDetectionReceived
   (معندهاش داتابيز ← مفيش outbox ← على RabbitMQ مباشرة)
                │
                ▼
② RabbitMQ: الـexchange بيحط نسخة في queue كل خدمة مشتركة
   (Pothole, Inventory, Violation, Building)
                │
                ▼
③ Pothole تسحب الرسالة من الـqueue بتاعها. الرسالة تعدّي على:
     CorrelationIdLogFilter → يحط CorrelationId في اللوجز        [الجزء 8]
     Retry                  → جاهز لو حصل exception             [الجزء 5.3]
     Inbox                  → "الرسالة دي جت قبل كده؟" لأ، كمّل  [الجزء 6]
                │
                ▼
④ RawDetectionReceivedConsumer.Consume()
     - لو مش road damage ← return (الخدمات التانية هتتعامل معاها)
     - _db.Potholes.Add(pothole)
     - PublishFollowUp(PotholeSaved)  → يتأكد من CorrelationId     [الجزء 7]
     - SaveChangesAsync()             → transaction واحدة:
                                        الحفرة + outbox_message + inbox_state
                │
                ▼
⑤ بعد ثانية بالكتير: MassTransit يلاقي PotholeSaved في outbox_message
   ويبعتها لـRabbitMQ                                             [الجزء 6]
                │
                ▼
⑥ RabbitMQ يوزّع PotholeSaved على CostCalculation و WorkOrder و Notification
   وكل واحدة تعيد نفس الدورة من ③
```

ولو حاجة باظت في النص؟

| اللي حصل | النتيجة |
|---|---|
| الـconsumer رمى exception | Retry لحد 4 محاولات، وبعدها الرسالة تروح `_error` |
| الخدمة وقعت قبل `SaveChangesAsync` | مفيش حاجة اتحفظت. RabbitMQ يبعت الرسالة تاني. |
| الخدمة وقعت بعد `SaveChangesAsync` وقبل الـack | RabbitMQ يبعتها تاني، الـinbox يتجاهلها. |
| RabbitMQ وقع بعد الحفظ | `PotholeSaved` مستنية في `outbox_message` لحد ما يرجع. |

في كل الحالات: **مفيش رسالة ضاعت، ومفيش حفرة اتسجلت مرتين.** ده الهدف من الفولدر كله.

---

## الجزء 10 — عايز أضيف consumer جديد، أعمل إيه؟

1. اعمل class في فولدر `Consumers/` بتاع الخدمة بتعمل `IConsumer<TheEvent>`.
2. سجّله في `Program.cs`:
   ```csharp
   builder.AddSmartCityBus(ServiceName, x =>
   {
       x.AddConsumer<MyNewConsumer>();
       x.AddSmartCityOutbox<MyDbContext>();
   });
   ```
3. لو هتبعت event بعده: اكتب `CorrelationId = msg.CorrelationId` و `SourceDetectionId = ...` في الـevent الجديد، وابعته بـ`context.PublishFollowUp(...)`.
4. نادي `_db.SaveChangesAsync()` **مرة واحدة** في الآخر.
5. متبلعش الـexceptions بـ`try/catch`.
6. شغّل الخدمة وبص على http://localhost:15672 — المفروض يظهر queue جديد.

---

## الجزء 11 — لما حاجة متشتغلش

| اللي شايفه | السبب غالباً |
|---|---|
| الخدمة بتقع أول ما تقوم: `Missing configuration value RabbitMQ:Username` | سكشن `RabbitMQ` ناقص في `appsettings` أو الـenvironment variables |
| `/health/ready` بيرجع Unhealthy | RabbitMQ مش شغال — `docker compose ps` |
| `CorrelationId must be copied unchanged` | نسيت `CorrelationId = msg.CorrelationId` |
| `SourceDetectionId must be set` | نسيت تحط `SourceDetectionId` |
| الـevent مبيوصلش ومفيش errors | نسيت `SaveChangesAsync` بعد الـpublish |
| رسايل في queue آخره `_error` | الـconsumer فشل 4 مرات. افتح الرسالة من الـUI أو دوّر بالـ`CorrelationId` في Seq |
| خدمة بتاخد نص الرسايل بس | خدمتين بنفس الـ`serviceName` ← بيشاركوا نفس الـqueue |

---

## قاموس

| الكلمة | المعنى |
|---|---|
| **Message Broker** | وسيط بيستلم الرسايل من خدمة ويوصّلها لخدمات تانية. عندنا: RabbitMQ. |
| **Event** | رسالة بتوصف حاجة حصلت خلاص (`PotholeSaved`). |
| **Publisher / Consumer** | اللي بيبعت / اللي بيستقبل. |
| **Exchange** | نقطة التوزيع. بيستلم الرسالة ويبعت نسخة لكل queue مربوط بيه. |
| **Queue** | طابور بيخزّن الرسايل لحد ما الـconsumer ياخدها. |
| **Binding** | الربط بين exchange و queue. |
| **Ack** | تأكيد من الـconsumer إنه خلّص الرسالة. |
| **At-least-once** | الرسالة توصل مرة **أو أكتر**، عمرها ما تضيع بس ممكن تتكرر. |
| **Idempotent** | لو اتنفذ مرتين، النتيجة زي مرة واحدة. |
| **Outbox** | جدول الرسايل الصادرة، بيتحفظ مع البيانات في نفس الـtransaction. |
| **Inbox** | جدول الرسايل الواردة اللي اتعالجت، عشان التكرار يتجاهل. |
| **Retry** | إعادة المحاولة لما الـconsumer يفشل (1s، 2s، 4s). |
| **DLQ / `_error` queue** | مكان الرسايل اللي فشلت بعد كل المحاولات. |
| **Filter** | كود بيتنفذ حوالين الـconsumer (زي middleware). |
| **MassTransit** | مكتبة .NET بتكلم RabbitMQ بدالنا. |
| **CorrelationId** | رقم الصورة الأصلية، بيتنسخ على كل event في السلسلة. |
| **EventId / MessageId** | رقم فريد للـevent، وبنستخدمه كـ`MessageId` عشان الـinbox يكشف التكرار. |
