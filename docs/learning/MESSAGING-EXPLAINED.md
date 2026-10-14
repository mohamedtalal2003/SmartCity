# شرح فولدر `Messaging` — RabbitMQ و MassTransit من الصفر

> الملف ده بيشرح الفولدر `src/BuildingBlocks/SmartCity.BuildingBlocks/Messaging/` (4 ملفات).
> مبني على الكود اللي على `main` بتاريخ 2026-10-07.
> لو إنت عارف RabbitMQ كفكرة عامة بس، ابدأ من الجزء 1. لو عارف الأساسيات، روح على الجزء 3 على طول.

---

## 1. الأساسيات: يعني إيه RabbitMQ أصلاً؟

### التشبيه: مكتب بريد

تخيل إن عندك 10 موظفين (خدمات) في شركة، وكل واحد عايز يبلّغ التانيين بحاجات حصلت.

- **من غير RabbitMQ:** كل موظف لازم يعرف مين محتاج الخبر، ويروح لكل واحد بنفسه. لو حد فيهم مش موجود (الخدمة واقعة) الخبر يضيع.
- **مع RabbitMQ:** الموظف بيرمي الخبر في **مكتب البريد** ويمشي. مكتب البريد عارف مين مشترك في النوع ده من الأخبار، وبيحط نسخة في **صندوق** كل واحد فيهم. لو حد مش موجود، الرسالة بتفضل مستنياه في صندوقه لحد ما يرجع.

ده بالظبط اللي بيحصل عندنا: خدمة Pothole بتقول "اتسجلت حفرة جديدة" (`PotholeSaved`)، ومش فارق معاها مين هيسمع. CostCalculation و WorkOrder و Notification كلهم مشتركين وكل واحد بياخد نسخته.

### المصطلحات الأساسية

| المصطلح | التشبيه | المعنى الحقيقي |
|---|---|---|
| **Message / Event** | الجواب | object صغير (JSON) بيوصف حاجة **حصلت خلاص**، زي `PotholeSaved`. |
| **Publisher** | اللي بيبعت الجواب | الخدمة اللي بتعمل `Publish`. مبتعرفش مين هيستلم. |
| **Consumer** | اللي بيقرا الجواب | class بتعمل `IConsumer<T>` وفيها method اسمها `Consume`. |
| **Exchange** | فرز البريد | المكان اللي الـpublisher بيرمي فيه الرسالة. مبيخزنش حاجة، بس بيوزّع نسخ على الـqueues المربوطة بيه. |
| **Queue** | صندوق البريد | طابور بيخزن الرسايل لحد ما الـconsumer ياخدها. لكل consumer queue خاص بيه. |
| **Binding** | الاشتراك | الربط بين exchange و queue: "أي حاجة تيجي على الـexchange ده، حط نسخة في الـqueue ده". |
| **Ack** | توقيع الاستلام | الـconsumer بيقول لـRabbitMQ "خلصت، امسح الرسالة". لو مقالش (crash)، الرسالة بترجع الطابور تاني. |
| **DLQ (Dead Letter Queue)** | صندوق "مرتجع" | المكان اللي الرسايل اللي فشلت تماماً بتروحله. عندنا اسمه `<queue>_error`. |

### شكلها عندنا في النظام

```
             Exchange: SmartCity.Contracts.Events.Detection:PotholeSaved
                                     │ (fanout = نسخة لكل واحد)
           ┌─────────────────────────┼──────────────────────────┐
           ▼                         ▼                          ▼
 queue: costcalculation-     queue: workorder-          queue: notification-
        pothole-saved               pothole-saved              pothole-saved
           │                         │                          │
           ▼                         ▼                          ▼
 PotholeSavedConsumer       PotholeSavedConsumer       PotholeSavedConsumer
 (CostCalculation)          (WorkOrder)                (Notification)
```

- **Exchange واحد لكل نوع event.** اسمه = الـnamespace + `:` + اسم الـclass.
- **Queue واحد لكل consumer في كل خدمة.** اسمه = اسم الخدمة + اسم الـconsumer من غير كلمة `Consumer`، بالـkebab-case.
- إنت **مش بتكتب ولا سطر** بيعمل exchange أو queue. MassTransit بيعملهم أوتوماتيك وقت تشغيل الخدمة.

> **جرّب بنفسك:** شغّل الـinfra وخدمة واحدة، وافتح http://localhost:15672 (smartcity/smartcity).
> في تاب **Exchanges** و **Queues** هتلاقي الأسماء دي بالظبط.

---

## 2. طب MassTransit دي إيه؟

RabbitMQ هو **السيرفر** (مكتب البريد). MassTransit هي **مكتبة .NET** بتخلّيك تتعامل معاه من غير ما تكتب كود RabbitMQ خام.

من غير MassTransit كنت هتكتب بنفسك: فتح connection، إنشاء exchanges و queues و bindings، تحويل الـobject لـJSON والعكس، الـack، الـretry، التعامل مع الـerrors...

مع MassTransit بتكتب سطرين:

```csharp
// الإرسال
await publishEndpoint.Publish(new PotholeSaved { ... });

// الاستقبال
public class PotholeSavedConsumer : IConsumer<PotholeSaved>
{
    public async Task Consume(ConsumeContext<PotholeSaved> context) { ... }
}
```

> ملحوظة من `CLAUDE.md`: لازم MassTransit **8.x**. الإصدار 9 بقى بفلوس (commercial license)، فممنوع نرقّي.

---

## 3. الفولدر فيه إيه؟ (نظرة سريعة)

| الملف | بيعمل إيه في جملة واحدة | مين بيستخدمه |
|---|---|---|
| `BusExtensions.cs` | بيوصّل الخدمة بـRabbitMQ وبيظبط كل الإعدادات المشتركة | **كل** الخدمات اللي بتبعت أو بتستقبل events |
| `OutboxExtensions.cs` | بيضمن إن الـDB والـevent يتحفظوا مع بعض، وإن الرسالة المكررة متتنفذش مرتين | الخدمات اللي ليها **database** بس |
| `PublishExtensions.cs` | الطريقتين **الوحيدتين** المسموح بيهم للـpublish | كل اللي بيبعت |
| `CorrelationIdLogFilter.cs` | بيحط الـ`CorrelationId` في كل سطر log جوه أي consumer | بيتركّب أوتوماتيك على كل consumer |

الفكرة إن كل خدمة متكتبش إعدادات RabbitMQ بنفسها. الكل بينادي نفس الـextension methods، فكل الخدمات بتتصرف بنفس الطريقة بالظبط.

---

## 4. `BusExtensions.cs` — توصيل الخدمة بـRabbitMQ

ده الملف الرئيسي. فيه method واحدة: `AddSmartCityBus(serviceName, configure)`.

### بتتنادى إزاي؟

خدمة **من غير database** (زي AiDetection):

```csharp
builder.AddSmartCityBus("AiDetection", x =>
{
    x.AddConsumer<FrameReceivedConsumer>();
});
```

خدمة **ليها database** (زي Pothole):

```csharp
builder.AddSmartCityBus("Pothole", x =>
{
    x.AddConsumer<RawDetectionReceivedConsumer>();
    x.AddSmartCityOutbox<PotholeDbContext>();   // ← من OutboxExtensions
});
```

### جوّاها بيحصل إيه؟ خطوة خطوة

**(أ) أسماء الـqueues**

```csharp
x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, false));
```

بيخلّي اسم الـqueue = اسم الخدمة + اسم الـconsumer بالـkebab-case.
مثال: الخدمة `Pothole` + الـconsumer `RawDetectionReceivedConsumer` ← queue اسمه زي `pothole-raw-detection-received`.

**ليه الـprefix مهم؟** لأن خدمات كتير عندها consumer بنفس الاسم (`PotholeSavedConsumer` موجود في CostCalculation و WorkOrder و Notification). لو من غير prefix، التلاتة هيبقوا على **نفس الـqueue**، وساعتها كل رسالة هتروح لواحد بس منهم (بيتقسموا الشغل بدل ما كل واحد ياخد نسخة). الـprefix بيدّي كل خدمة صندوق خاص بيها.

**(ب) Health check**

```csharp
x.ConfigureHealthCheckOptions(o => { o.Name = "rabbitmq"; o.Tags.Add(HealthCheckExtensions.ReadyTag); });
```

بيخلّي `/health/ready` يقول "مش جاهز" لو الخدمة مش متوصلة بـRabbitMQ.

**(ج) الـfilters اللي بتتركب على كل consumer**

```csharp
x.AddConfigureEndpointsCallback((context, _, endpoint) =>
{
    endpoint.UseConsumeFilter(typeof(CorrelationIdLogFilter<>), context);
    endpoint.UseMessageRetry(r => r.Intervals(
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
});
```

"Filter" يعني كود بيتنفذ **حوالين** الـconsumer بتاعك، زي الـmiddleware في ASP.NET. هنا فيه اتنين:

1. **CorrelationIdLogFilter** — (شرحه في الجزء 7).
2. **Retry** — لو الـconsumer رمى exception:
   ```
   محاولة 1 ✗ ← استنى 1s ← محاولة 2 ✗ ← استنى 2s ← محاولة 3 ✗ ← استنى 4s ← محاولة 4 ✗
                                                                              │
                                                       الرسالة تتنقل لـ pothole-raw-detection-received_error
   ```
   يعني 4 محاولات إجمالي، وبعدها الرسالة **مش بتضيع** — بتروح الـ`_error` queue (الـDLQ) وتقدر تشوفها من الـRabbitMQ UI وتعيد إرسالها بعد ما تصلّح المشكلة.

**ليه الـcallback ده متسجل *قبل* `configure`؟** الترتيب مهم. الـfilters بتتلف حوالين بعض زي البصلة. لما بنسجّلهم الأول، بيبقوا **برّه** الـoutbox:

```
CorrelationIdLogFilter        ← الأول: كل اللوجز تحت بقى فيها CorrelationId
  └─ Retry                    ← كل محاولة retry بتعيد الـtransaction كلها من الأول
       └─ EF Outbox/Inbox     ← (من OutboxExtensions)
            └─ Consumer بتاعك
```

**(د) الاتصال بـRabbitMQ**

```csharp
cfg.Host(rabbit["Host"] ?? "localhost", rabbit["VirtualHost"] ?? "/", h =>
{
    h.Username(Required(rabbit, "Username"));
    h.Password(Required(rabbit, "Password"));
});
```

الإعدادات جاية من `appsettings.json` (سكشن `RabbitMQ`). الـusername والـpassword **إجباريين**: لو مش موجودين الخدمة بتقع فوراً برسالة واضحة بدل ما تحاول تتوصل بباسورد غلط. (القاعدة: مفيش secrets في الكود.)

**(هـ) شكل الـJSON على السلك**

```csharp
json.PropertyNamingPolicy = null;                  // PascalCase زي ما هي في C#
json.Converters.Add(new JsonStringEnumConverter()); // enums كـ "High" مش 2
```

ده بيضمن إن الرسالة شكلها بالظبط زي `src/SmartCity.Contracts/MockMessages/mock-events.json`.
الـenums كـ strings مهمة: لو حد غيّر ترتيب الـenum، الأرقام هتبوظ لكن الأسماء لأ، واللوجز بتتقري بسهولة.

**(و) استبعاد الـbase class**

```csharp
cfg.Publish<IntegrationEvent>(p => p.Exclude = true);
```

كل الـevents بتورث من `IntegrationEvent`. MassTransit افتراضياً بيعمل exchange للـbase class كمان، وأي event يتبعت بيروح هناك برضه. إحنا مش محتاجين exchange "كل حاجة" ده، فبنقفله.

**(ز) إنشاء كل حاجة**

```csharp
cfg.ConfigureEndpoints(context);
```

السطر ده هو اللي بيبص على كل consumer متسجل، ويعمل الـqueue بتاعه والـexchange والـbinding بينهم.

---

## 5. `OutboxExtensions.cs` — المشكلة اللي بيحلها أهم من الكود نفسه

### المشكلة 1: الحفظ والإرسال مش atomic

تخيل الـconsumer ده من غير outbox:

```csharp
_db.Potholes.Add(pothole);
await _db.SaveChangesAsync();        // ✓ الحفرة اتحفظت
await context.Publish(new PotholeSaved { ... });  // 💥 RabbitMQ وقع / الخدمة crashed
```

النتيجة: الحفرة في الداتابيز، بس محدش عرف بيها. لا اتحسبت تكلفتها، ولا اتعمل work order. **بيانات ضايعة بصمت.**

والعكس أوحش: لو بعتّ الأول وبعدين الـDB فشل، الخدمات التانية هتشتغل على حفرة **مش موجودة**.

### الحل: Transactional Outbox

بدل ما نبعت لـRabbitMQ مباشرة، بنكتب الـevent في **جدول في نفس الداتابيز** (`outbox_message`)، **في نفس الـtransaction** بتاعة الحفرة:

```
┌──────────── Transaction واحدة في pothole_db ────────────┐
│  INSERT INTO potholes ...                              │
│  INSERT INTO outbox_message (PotholeSaved JSON) ...    │
└──────────────────── COMMIT ────────────────────────────┘
                         │
                         │  (background service في MassTransit بيبص على الجدول كل ثانية)
                         ▼
                 بيبعت الرسالة لـRabbitMQ ← لو نجح يمسحها من الجدول
```

يا الاتنين يتحفظوا، يا الاتنين لأ. ولو RabbitMQ واقع، الرسالة بتستنى في الجدول وتتبعت لما يرجع.

### المشكلة 2: الرسالة ممكن توصل مرتين

RabbitMQ بيضمن **at-least-once** — يعني "مرة على الأقل"، مش "مرة بالظبط". مثال: الـconsumer خلّص وحفظ، وقبل ما يبعت الـack الخدمة وقعت. RabbitMQ مش عارف إنها خلصت، فبيبعت الرسالة تاني ← **حفرتين** بدل واحدة.

### الحل: Inbox

جدول `inbox_state` بيسجل الـ`MessageId` بتاع كل رسالة اتعالجت. لو نفس الـ`MessageId` جه تاني، MassTransit بيتجاهله من غير ما ينادي الـconsumer أصلاً. (عشان كده `PublishExtensions` بيخلّي `MessageId = EventId` — شوف الجزء 6.)

### الكود

```csharp
x.AddEntityFrameworkOutbox<TDbContext>(o =>
{
    o.UsePostgres();                         // SQL locking مناسب لـPostgres
    o.UseBusOutbox();                        // حتى الـpublish من gRPC handler يروح الـoutbox
    o.QueryDelay = TimeSpan.FromSeconds(1);  // كل قد إيه يبص على الجدول
});

x.AddConfigureEndpointsCallback((context, _, endpoint) =>
    endpoint.UseEntityFrameworkOutbox<TDbContext>(context));   // inbox + outbox حوالين كل consumer
```

و`AddSmartCityOutboxEntities()` بيضيف التلات جداول للـDbContext، ولازم يتنادى في `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddSmartCityOutboxEntities();   // inbox_state, outbox_message, outbox_state
    ...
}
```

| الجدول | وظيفته |
|---|---|
| `inbox_state` | الرسايل اللي اتعالجت قبل كده (عشان منع التكرار) |
| `outbox_message` | الرسايل اللي مستنية تتبعت لـRabbitMQ |
| `outbox_state` | حالة الإرسال (lock + آخر رسالة اتبعتت) |

### حالتين لاستخدام الـoutbox

1. **جوه consumer** (زي `RawDetectionReceivedConsumer` في Pothole): أي publish بيتحفظ مع الـ`SaveChangesAsync` بتاع الـconsumer.
2. **جوه gRPC write handler** (زي `WorkOrderCommandGrpcService`): مفيش رسالة جاية، بس `UseBusOutbox()` بيخلّي `IPublishEndpoint` يكتب في الجدول برضه. لازم تنادي `SaveChangesAsync` **مرة واحدة** بعد الـpublish عشان الاتنين يتحفظوا مع بعض.

### ⚠️ الخدمات اللي **مفيهاش** outbox

`AiDetection` و `VehicleCommunication` مالهمش database (stateless)، فمفيش جدول يتحط فيه outbox. الـpublish عندهم بيروح لـRabbitMQ **مباشرة**. ده مقبول لأنهم مش بيحفظوا حاجة، فمفيش "DB و event لازم يتفقوا".

---

## 6. `PublishExtensions.cs` — الطريقتين الوحيدتين للإرسال

القاعدة: **متستخدمش `Publish` العادية بتاعة MassTransit مباشرة.** استخدم واحدة من دول:

### `PublishFollowUp` — لما تبعت event *بسبب* event جالك

```csharp
// جوه consumer
await context.PublishFollowUp(saved, context.CancellationToken);
```

بتعمل 3 حاجات:

1. **بتتأكد إن `CorrelationId` اتنسخ زي ما هو.** لو نسيت تنسخه أو حطيت قيمة تانية ← exception فوراً.
2. **بتتأكد إن `SourceDetectionId` مش فاضي** (لو الـevent عنده الـproperty دي).
3. **بتحط `MessageId = EventId`** (عشان الـinbox).

**ليه التشدد ده؟** الـ`CorrelationId` هو الـ`FrameId` الأصلي بتاع الصورة. لما كل event في السلسلة بيشيله، تقدر تكتب في Seq `CorrelationId = '<id>'` وتشوف القصة كاملة:

```
FrameReceived ──► RawDetectionReceived ──► PotholeSaved ──► CostEstimateGenerated ──► WorkOrderCreated
   └──────────────────── كلهم نفس الـ CorrelationId ────────────────────────────────────────┘
```

لو خدمة واحدة نسيت تنسخه، السلسلة تتقطع ومتقدرش تتتبع. فبدل ما نعتمد إن كل حد يفتكر، الكود بيرفض.

### `PublishEvent` — لما تبعت event *من غير* ما يكون جالك event

```csharp
// جوه gRPC handler أو MQTT subscriber
await publishEndpoint.PublishEvent(frameReceived);
```

بتُستخدم في أول السلسلة (VehicleCommunication بتبعت `FrameReceived` لما صورة توصل من MQTT) أو بعد أمر من اليوزر (WorkOrder بتبعت `WorkOrderStatusChanged` لما حد يغيّر الحالة من الـUI). بتحط `MessageId = EventId` بس، لأن مفيش event قبلها تنسخ منه.

### ليه `MessageId = EventId` مهم؟

MassTransit بيعمل `MessageId` عشوائي لكل publish. لو اتبعت نفس الـevent مرتين (مثلاً الـoutbox بعته وبعدين وقع قبل ما يمسحه من الجدول)، كل مرة هيبقى ليها `MessageId` مختلف، والـinbox **مش هيعرف** إنها نفس الرسالة. لما نثبّته على `EventId` (اللي جوه الرسالة نفسها)، التكرار بيتكشف.

---

## 7. `CorrelationIdLogFilter.cs` — CorrelationId في كل سطر log

### المشكلة

جوه أي consumer بتكتب:

```csharp
_logger.LogInformation("Saved pothole {PotholeId}", pothole.Id);
```

السطر ده في Seq مش هيبقى فيه أي حاجة تقولك هو تبع أنهي صورة. ولو عايز كل consumer يكتب `CorrelationId` بإيده في كل log، أكيد حد هينسى.

### الحل

Filter بيشتغل **قبل** الـconsumer، بيطلّع الـ`CorrelationId` من الرسالة، ويحطه في الـ"log context" بتاع Serilog:

```csharp
using (LogContext.PushProperty("CorrelationId", correlationId))
using (LogContext.PushProperty("EventId", ...))
using (LogContext.PushProperty("MessageType", typeof(T).Name))
{
    await next.Send(context);   // ← هنا الـconsumer بتاعك بيشتغل
}
```

أي log بيتكتب جوه الـ`using` (يعني أي log جوه الـconsumer، حتى لو في method تانية بيناديها) بياخد التلات properties دول أوتوماتيك. لما الـconsumer يخلص، الـ`using` بيشيلهم.

النتيجة في Seq: تكتب `CorrelationId = 'xxxx'` وتلاقي كل اللوجز من كل الخدمات للصورة دي، مترتبة بالوقت.

> الـfilter ده **generic** (`<T>`)، و`BusExtensions` بيركّبه على كل consumer بـ`typeof(CorrelationIdLogFilter<>)`، فإنت مش محتاج تعمل أي حاجة عشان يشتغل.

---

## 8. رحلة رسالة واحدة من الأول للآخر

خلّينا نمشي ورا detection واحد لحد ما يبقى حفرة متسجلة:

```
1. AiDetection: FrameReceivedConsumer
   └─ context.PublishFollowUp(RawDetectionReceived)
      └─ (مفيش outbox) ← على RabbitMQ مباشرة
         └─ Exchange: SmartCity.Contracts.Events.Ingestion:RawDetectionReceived
             ├─► queue pothole-raw-detection-received
             ├─► queue inventory-raw-detection-received
             ├─► queue violation-raw-detection-received
             └─► queue building-raw-detection-received

2. Pothole بتسحب الرسالة من الـqueue بتاعها
   ├─ CorrelationIdLogFilter  ← يحط CorrelationId في اللوجز
   ├─ Retry                   ← جاهز لو حاجة فشلت
   ├─ Inbox                   ← "الـMessageId ده اتعالج قبل كده؟" لأ ← كمّل
   └─ RawDetectionReceivedConsumer.Consume()
        ├─ _db.Potholes.Add(pothole)
        ├─ context.PublishFollowUp(PotholeSaved) ← يتأكد من CorrelationId + SourceDetectionId
        └─ _db.SaveChangesAsync()
             └─ Transaction واحدة: potholes + outbox_message + inbox_state

3. Outbox delivery service في Pothole (كل ثانية)
   └─ يلاقي PotholeSaved في outbox_message ← يبعتها لـRabbitMQ ← يمسحها

4. RabbitMQ يوزّع PotholeSaved على:
   costcalculation-pothole-saved, workorder-pothole-saved, notification-pothole-saved
   ← وكل واحد بيعيد نفس الدورة من خطوة 2
```

---

## 9. إزاي تضيف consumer جديد (checklist)

1. اعمل class بتعمل `IConsumer<TheEvent>` في فولدر `Consumers/` بتاع الخدمة.
2. سجّله في `Program.cs` جوه `AddSmartCityBus`:
   ```csharp
   x.AddConsumer<MyNewConsumer>();
   ```
3. لو هتبعت event بعده ← `context.PublishFollowUp(...)`، وانسخ `CorrelationId` من الرسالة الجاية، وحط `SourceDetectionId`.
4. لو الخدمة ليها DB ← نادي `SaveChangesAsync` في الآخر (الـevent والـdata هيتحفظوا مع بعض).
5. **متعملش try/catch يبلع الـexceptions.** سيب الـexception يطلع، عشان الـretry والـ`_error` queue يشتغلوا.
6. شغّل الخدمة وبص على http://localhost:15672 ← المفروض تلاقي queue جديد باسم `<service>-<consumer-name>`.

---

## 10. مشاكل شائعة

| العَرَض | السبب المحتمل |
|---|---|
| الخدمة بتقع أول ما تقوم بـ`Missing configuration value RabbitMQ:Username` | سكشن `RabbitMQ` ناقص في `appsettings.json` أو الـenv vars. |
| `/health/ready` بيرجع Unhealthy | RabbitMQ مش شغال. جرّب `docker compose ps`. |
| exception فيها `CorrelationId must be copied unchanged` | نسيت تكتب `CorrelationId = msg.CorrelationId` في الـevent الجديد. |
| exception فيها `SourceDetectionId must be set` | نسيت `SourceDetectionId = ...` في الـevent الجديد. |
| الـevent مش بيوصل للخدمات التانية رغم إن مفيش errors | غالباً نسيت `SaveChangesAsync` في خدمة فيها outbox، فالرسالة مدخلتش جدول `outbox_message` أصلاً. |
| رسايل متكدسة في queue اسمه `..._error` | الـconsumer فشل 4 مرات. افتح الرسالة من الـRabbitMQ UI وشوف الـexception في الـheaders، أو دوّر في Seq. |
| الخدمة تانية بتاخد نص الرسايل بس | خدمتين بيستخدموا **نفس اسم الـqueue** (نفس `serviceName`). اتأكد إن كل خدمة بتبعت اسم مختلف لـ`AddSmartCityBus`. |

---

## 11. قاموس سريع

| الكلمة | المعنى |
|---|---|
| **At-least-once** | الرسالة هتوصل مرة **أو أكتر**. عشان كده لازم الـconsumers يبقوا idempotent. |
| **Idempotent** | لو اتنفذ مرتين النتيجة زي مرة واحدة. |
| **Outbox** | جدول في DB الخدمة بتتحط فيه الرسايل الصادرة قبل ما تتبعت. |
| **Inbox** | جدول بيسجل الرسايل الواردة اللي اتعالجت عشان التكرار يتجاهل. |
| **Receive endpoint** | اسم MassTransit للـqueue + الـconsumer اللي بيقرا منه. |
| **Fanout** | نوع exchange بيبعت نسخة لكل queue مربوط بيه من غير أي فلترة. |
| **CorrelationId** | الـ`FrameId` الأصلي، بيعدّي على كل event في السلسلة عشان التتبع. |
| **SourceDetectionId** | الـ`DetectionId` اللي السلسلة بدأت منه. |
| **EventId** | ID فريد لكل event. بنستخدمه كـ`MessageId` عشان الـinbox. |
