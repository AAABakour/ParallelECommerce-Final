# تقرير التسليم المرحلي - أول 5 متطلبات

**اسم المشروع:** High-Performance E-Commerce Backend Engine  
**التقنية المستخدمة:** .NET Minimal API  
**نطاق التقرير الحالي:** أول خمسة متطلبات من مشروع البرمجة المتوازية  
**ملف اختبار JMeter الرسمي:** `Tests/JMeter/ParallelECommerce_First5.jmx`

---

## 1. ملخص تنفيذي

يركز المشروع على بناء Backend لمنظومة تجارة إلكترونية قادرة على التعامل مع طلبات متزامنة، مع إثبات المفاهيم غير الوظيفية التي تمت دراستها: حماية البيانات، التحكم بالموارد، المعالجة غير المتزامنة، المعالجة على دفعات، وتوزيع الأحمال.

في هذه المرحلة تم تنفيذ وتقوية أول خمسة متطلبات، وتجهيز ملف JMeter رسمي لاختبارها، مع صورة نتائج التنفيذ. نتيجة التشغيل الحالية في JMeter أظهرت أن جميع الطلبات نفذت بنجاح، حيث كانت قيمة:

```text
Error % = 0.00%
```

أي أن الاختبار لم يسجل أي فشل في الطلبات.


---

## 2. بنية المشروع

البنية الحالية مقسمة بشكل واضح إلى:

```text
Endpoints/      نقاط الدخول للـ API
Services/       منطق المعالجة والتزامن والـ queues والـ batch والـ load balancing
Models/         النماذج المستخدمة في النتائج والقياسات
Middleware/     طبقة AOP لمراقبة الأداء
Docs/           التوثيق والتقرير والصور
Tests/JMeter/   ملفات JMeter الرسمية
```

اعتمدنا على فصل المسؤوليات بحيث لا تكون كل المعالجة داخل الـ endpoints مباشرة، بل يتم وضع المنطق المهم داخل services مستقلة قابلة للاختبار.

---

## 3. مراقبة الأداء وتطبيق AOP

تمت إضافة طبقة مراقبة عامة باستخدام:

```text
Middleware/PerformanceMonitoringMiddleware.cs
Services/PerformanceMetricsService.cs
Services/ResourceMonitoringService.cs
```

هذه الطبقة تعمل كـ AOP لأنها تلتف حول كل HTTP request وتقيس زمن التنفيذ وحالة الطلب بدون تكرار كود القياس داخل كل endpoint.

### Endpoints المراقبة

```http
GET  /monitoring/metrics
GET  /monitoring/resources
GET  /monitoring/resources/csv
POST /monitoring/reset
```

### ماذا تقيس؟

- عدد الطلبات الكلي.
- عدد الطلبات الفاشلة.
- زمن الاستجابة لكل endpoint.
- الطلبات النشطة حالياً.
- استهلاك الذاكرة.
- عدد Threads.
- مؤشرات ThreadPool.
- عينات الموارد عبر الزمن.

هذا يدعم متطلب القياس ويعطي أساساً لرسم مخططات الموارد لاحقاً عند أخذ الصور النهائية.

---

# Requirement 01 - حماية البيانات المشتركة من التضارب

## المشكلة

في منظومة التجارة الإلكترونية، المخزون مورد مشترك يمكن أن يصل إليه عدة مستخدمين في نفس اللحظة. إذا حاول 20 مستخدماً شراء منتج كميته 10 فقط بدون أي حماية، قد تقرأ عدة Threads نفس الكمية قبل تحديثها، مما يؤدي إلى بيع كمية أكبر من المخزون المتاح.

## الحلول المقترحة

| الحل | التقييم |
|---|---|
| بدون حماية | سريع ظاهرياً لكنه غير صحيح ويسبب Race Condition |
| `lock` محلي | مناسب للمحاكاة داخل instance واحدة ويمنع التضارب |
| Database Transaction / Row Lock | أنسب للإنتاج عند وجود قاعدة بيانات حقيقية |
| Optimistic Locking | مناسب للأنظمة كثيرة القراءة قليلة التصادم |

## هل البنية تدعم الحل؟

نعم. تم عزل منطق المخزون داخل:

```text
Services/InventoryService.cs
Endpoints/InventoryEndpoints.cs
```

وهذا يجعل نقطة التزامن واضحة وقابلة للشرح والتعديل لاحقاً عند الانتقال إلى قاعدة بيانات.

## الحل المختار

تم اختيار `lock` داخل النسخة المحسنة لإثبات مبدأ حماية القسم الحرج Critical Section.

## سبب الاختيار

الهدف الحالي هو إثبات Race Condition ثم إثبات منعها ضمن بيئة بسيطة. استخدام `lock` يعطي نتيجة واضحة جداً في الاختبار: قبل الحل يصبح المخزون خاطئاً، وبعد الحل يصبح المخزون صحيحاً.

## الاختبار

Endpoints المستخدمة:

```http
POST /demo/race/before
POST /demo/race/after
```

### نتيجة قبل الحل

عند وجود مخزون ابتدائي 10 وتشغيل 20 مستخدماً متزامناً:

```text
successCount = 20
failedCount = 0
stockQuantity = -10
```

هذا يثبت حدوث Race Condition لأن النظام باع 20 قطعة رغم أن المخزون 10 فقط.

### نتيجة بعد الحل

بعد استخدام `lock`:

```text
successCount = 10
failedCount = 10
stockQuantity = 0
```

هذا يثبت أن النظام سمح فقط بعدد عمليات شراء يساوي المخزون المتاح، ومنع تخريب البيانات.

---

# Requirement 02 - إدارة الموارد والتحكم بالسعة

## المشكلة

لا يجوز للنظام أن يشغل عدداً غير محدود من العمليات الثقيلة في نفس اللحظة، لأن ذلك قد يؤدي إلى استهلاك CPU وMemory وThreads أو اتصالات قاعدة البيانات بشكل مفرط. وفي نفس الوقت لا يجب تقليل التوازي بشكل مبالغ فيه حتى لا تصبح الاستجابة بطيئة جداً.

## الحلول المقترحة

| الحل | المزايا | العيوب |
|---|---|---|
| بدون حد للتوازي | سريع عند الحمل الصغير | خطر تحت الضغط العالي |
| `SemaphoreSlim` | حد واضح وقابل للقياس للعمليات النشطة | قد يزيد زمن الانتظار |
| Queue + Workers | ممتاز للمهام الخلفية | غير مناسب لكل عملية يجب أن تكتمل قبل الرد |

## هل البنية تدعم الحل؟

نعم. منطق التحكم بالسعة موجود داخل:

```text
Services/CapacityControlService.cs
Endpoints/CapacityControlEndpoints.cs
```

كما أن طبقة المراقبة تسجل زمن الاستجابة والموارد أثناء الاختبار.

## الحل المختار

تم اختيار `SemaphoreSlim` بحد أقصى:

```text
MaxParallelOperations = 5
```

## سبب الاختيار

لأنه يعطي حداً مباشراً وواضحاً لعدد العمليات الثقيلة التي يمكن أن تعمل بالتوازي. هذا يثبت أن النظام لا يستهلك الموارد بشكل مفتوح.

## الاختبار

Endpoints المستخدمة:

```http
POST /capacity/reset
POST /capacity/before/work
POST /capacity/after/work
GET  /capacity/metrics
```

كما توجد endpoints تجريبية مختصرة:

```http
POST /demo/capacity/before
POST /demo/capacity/after
```

### نتيجة قبل الحل

في تجربة 50 عملية ثقيلة:

```text
totalRequests = 50
maxActiveOperationsObserved = 50
```

هذا يعني أن كل العمليات اشتغلت معاً بدون حد، وهو خطر عند زيادة الحمل.

### نتيجة بعد الحل

بعد تطبيق `SemaphoreSlim`:

```text
totalRequests = 50
maxAllowedParallelOperations = 5
maxActiveOperationsObserved = 5
```

هذا يثبت أن النظام لم يسمح بأكثر من 5 عمليات ثقيلة في نفس اللحظة.

## trade-off الهندسي

زمن التنفيذ بعد الحل قد يصبح أطول لأن بعض الطلبات تنتظر دورها، لكن هذا مقبول لأن الهدف هو حماية النظام من الانهيار تحت الضغط. الحل يبدل جزءاً من زمن الانتظار مقابل استقرار أعلى واستهلاك موارد مضبوط.

---

# Requirement 03 - المعالجة غير المتزامنة باستخدام Queue

## المشكلة

بعض المهام مثل إرسال الفاتورة أو الإشعار لا يحتاج المستخدم إلى انتظارها. إذا بقيت هذه المهام داخل مسار الطلب الرئيسي، يصبح زمن استجابة المستخدم عالياً، وتبقى Threads مشغولة لفترة أطول.

## الحلول المقترحة

| الحل | التقييم |
|---|---|
| معالجة متزامنة | بسيطة لكنها بطيئة للمستخدم |
| RabbitMQ / Kafka | حل إنتاجي قوي لكنه أثقل من المطلوب حالياً |
| In-Process Queue + BackgroundService | مناسب للمشروع ويثبت المفهوم بدون تعقيد خارجي |

## هل البنية تدعم الحل؟

نعم. تم عزل queue والـ worker في:

```text
Services/NotificationQueueService.cs
Services/NotificationWorkerService.cs
Models/NotificationJob.cs
```

## الحل المختار

تم استخدام:

```text
Channel<NotificationJob>
BackgroundService
```

مع جعل الـ Queue محدودة السعة لتجنب استهلاك ذاكرة غير محدود.

## سبب الاختيار

هذا الحل يثبت الفرق بين synchronous و asynchronous processing بشكل واضح، بدون الحاجة إلى تشغيل RabbitMQ أو Kafka في التسليم المرحلي.

## الاختبار

Endpoints المستخدمة:

```http
POST /notifications/reset
POST /orders/before-sync
POST /orders/after-async
GET  /notifications/status
```

### نتيجة قبل الحل

في النسخة المتزامنة، المستخدم ينتظر إنشاء الطلب ثم إرسال الإشعار. في اختبار JMeter ظهر أن متوسط زمن الطلب المتزامن كان تقريباً:

```text
REQ03 BEFORE average ≈ 3353 ms
```

### نتيجة بعد الحل

في النسخة غير المتزامنة، يتم وضع الإشعار في Queue والرد على المستخدم بسرعة. في اختبار JMeter ظهر أن متوسط زمن الطلب غير المتزامن كان تقريباً:

```text
REQ03 AFTER average ≈ 323 ms
```

وهذا يثبت أن نقل المهمة الثانوية إلى الخلفية قلل زمن استجابة المستخدم بشكل واضح.

---

# Requirement 04 - معالجة البيانات الضخمة على دفعات

## المشكلة

جرد المبيعات اليومية أو معالجة التقارير قد يتضمن عدداً كبيراً من السجلات. المعالجة المتسلسلة سجل بعد سجل تصبح بطيئة جداً كلما زاد حجم البيانات.

## الحلول المقترحة

| الحل | التقييم |
|---|---|
| Sequential Processing | بسيط لكنه بطيء |
| تشغيل كل السجلات بالتوازي | سريع ظاهرياً لكنه قد يستهلك الموارد بشدة |
| تقسيم إلى Chunks مع حد للتوازي | يوازن بين الأداء والتحكم بالموارد |

## هل البنية تدعم الحل؟

نعم. تمت إضافة بنية خاصة للـ Batch Jobs:

```text
Services/BatchProcessingService.cs
Services/BatchJobWorkerService.cs
Models/BatchJobRequest.cs
Models/BatchJobSnapshot.cs
Models/BatchChunkSnapshot.cs
Models/BatchMetricsSnapshot.cs
```

## الحل المختار

تم اختيار:

```text
Background Job + Chunk Processing + bounded parallel chunks
```

## سبب الاختيار

لأنه يحقق شرطين معاً:

1. تقليل زمن معالجة البيانات مقارنة بالمعالجة المتسلسلة.
2. منع فتح عدد غير محدود من المهام المتوازية.

## الاختبار

Endpoints المستخدمة:

```http
POST /batch/reset
POST /batch/before/sequential
POST /batch/after/start-daily-sales-job
GET  /batch/jobs
GET  /batch/metrics
```

### نتيجة قبل الحل

في اختبار JMeter ظهر أن المعالجة المتسلسلة أخذت تقريباً:

```text
REQ04 BEFORE average ≈ 3153 ms
```

### نتيجة بعد الحل

بعد الحل، endpoint الخاص ببدء الـ background job رجع بسرعة لأنه لم ينتظر انتهاء الجرد كاملاً:

```text
REQ04 AFTER start job average ≈ 18 ms
```

ثم تتم معالجة الـ chunks بالخلفية، ويمكن متابعة الحالة من `/batch/jobs` و`/batch/metrics`.

## ملاحظة هندسية

زمن endpoint بعد الحل لا يعني أن الجرد انتهى خلال 18ms، بل يعني أن API أعاد `jobId` بسرعة وترك التنفيذ للخلفية. هذا هو المقصود من Background Job.

---

# Requirement 05 - توزيع الأحمال

## المشكلة

إرسال كل الطلبات إلى خادم واحد يجعل هذا الخادم نقطة اختناق، بينما تبقى الخوادم الأخرى غير مستخدمة. في منظومة تجارة إلكترونية تحت ضغط عالٍ، هذا يضعف الاستقرار ويزيد زمن الاستجابة.

## الحلول المقترحة

| الحل | التقييم |
|---|---|
| Single Server | بسيط لكنه غير قابل للتوسع |
| Round Robin | مناسب عندما تكون الخوادم متشابهة |
| Least Connections | أفضل للطلبات متفاوتة المدة لكنه أعقد |
| Weighted Round Robin | مناسب إذا كانت الخوادم مختلفة القوة |

## هل البنية تدعم الحل؟

نعم. تم عزل منطق توزيع الأحمال داخل:

```text
Services/LoadBalancingService.cs
Endpoints/LoadBalancingEndpoints.cs
Models/ServerNode.cs
```

## الحل المختار

تم اختيار:

```text
Round Robin over healthy servers
```

مع دعم Health Check مبسط بحيث لا يتم إرسال الطلبات إلى خادم غير صحي.

## سبب الاختيار

لأن الخوادم في المحاكاة متشابهة، وRound Robin يحقق توزيعاً عادلاً وسهل القياس.

## الاختبار

Endpoints المستخدمة:

```http
POST /load-balancer/reset
POST /load-balancer/before/request
POST /load-balancer/after/request
GET  /load-balancer/metrics
POST /demo/load-balancing/before
POST /demo/load-balancing/after
POST /demo/load-balancing/after-health-check
```

### نتيجة قبل الحل

كل الطلبات تذهب إلى Server A:

```text
Server A: يستقبل كل الحمل
Server B: 0
Server C: 0
```

### نتيجة بعد الحل

يتم توزيع الطلبات بالتتابع على الخوادم الصحية:

```text
Request 1 -> Server A
Request 2 -> Server B
Request 3 -> Server C
Request 4 -> Server A
...
```

### Health Check

عند جعل Server B غير صحي، يتجاهله الـ Load Balancer ويرسل الطلبات إلى Server A وServer C فقط.

---

# اختبار JMeter الرسمي لأول 5 متطلبات

## ملف JMX المطلوب

تم تجهيز ملف JMeter الرسمي في:

```text
Tests/JMeter/ParallelECommerce_First5.jmx
```

هذا هو الملف المطلوب إرفاقه للتسليم.

## ماذا يغطي الاختبار؟

| المتطلب | جزء JMeter |
|---|---|
| Requirement 01 - Race Condition | `01A`, `01B` |
| Requirement 02 - Capacity Control | `02A` إلى `02F` |
| Requirement 03 - Async Queue | `03A` إلى `03E` |
| Requirement 04 - Batch Processing | `04A` إلى `04D` |
| Requirement 05 - Load Balancing | `05A` إلى `05D` |
| Monitoring | `99 - Final monitoring metrics and resources` |

## نتيجة التنفيذ

تم تشغيل الاختبار من Apache JMeter GUI. أظهرت نتيجة Summary Report:

```text
Error % = 0.00%
```

كما أظهر View Results Tree أن الطلبات ناجحة باللون الأخضر.

## صور الإثبات

تم حفظ صور التنفيذ داخل:

```text
Docs/Screenshots/jmeter_first5_summary_report.PNG
Docs/Screenshots/jmeter_first5_view_results_tree.PNG
```

### صورة Summary Report

![JMeter Summary Report](Screenshots/jmeter_first5_summary_report.PNG)

### صورة View Results Tree

![JMeter View Results Tree](Screenshots/jmeter_first5_view_results_tree.PNG)

---

# قابلية البنية للمتطلبات اللاحقة

رغم أن التسليم الحالي يركز على أول 5 متطلبات، تم الانتباه إلى أن البنية يجب أن تبقى مناسبة لباقي المشروع:

- تم فصل الخدمات عن endpoints حتى يمكن تطوير متطلبات لاحقة دون تغيير كبير في بنية أول خمسة متطلبات.
- تم تجهيز طبقة monitoring/AOP التي ستفيد Requirement 10 الخاص بالـ Benchmarking.
- تم استخدام services مستقلة تجعل إضافة Optimistic/Pessimistic Locking أو Database Transaction أسهل لاحقاً.
- تم استخدام queues وbackground workers بطريقة قابلة للتوسيع لاحقاً إلى RabbitMQ أو Kafka.
- تم جعل Capacity Control وBatch Processing محدودين بسعة واضحة حتى لا تتحول المعالجة المتوازية إلى استهلاك مفرط للموارد.

---

# الخلاصة النهائية

تم تنفيذ أول 5 متطلبات بشكل قابل للاختبار والشرح:

| المتطلب | الحالة |
|---|---|
| حماية البيانات من Race Condition | منجز |
| إدارة الموارد والتحكم بالسعة | منجز |
| المعالجة غير المتزامنة باستخدام Queue | منجز |
| Batch Processing كـ Background Job وChunks | منجز |
| Load Balancing باستخدام Round Robin وHealth Check | منجز |
| JMeter JMX لأول 5 متطلبات | منجز |
| صورة نتائج التنفيذ | منجزة |

المخرج الأساسي لهذه المرحلة:

```text
Tests/JMeter/ParallelECommerce_First5.jmx
Docs/Screenshots/jmeter_first5_summary_report.PNG
Docs/Screenshots/jmeter_first5_view_results_tree.PNG
Docs/First5_Delivery_Report.md
```

---

# Additional Final Notes - Real Database Requirements 07 and 08

After the original first-five delivery report, the project was extended with real SQL Server implementations for Requirements 07 and 08:

- Requirement 07 now includes `Docs/Requirement07_RealDatabase_OptimisticLocking.md`, using EF Core `RowVersion` optimistic concurrency to prevent inventory overselling.
- Requirement 08 now includes `Docs/Requirement08_RealDatabase_ACIDTransaction.md`, using a real EF Core SQL Server transaction so payment, stock update, and order creation commit or rollback together.
- The old in-memory and Redis-based demos were kept working. The new real database endpoints are separate Swagger groups.

Manual verification commands:

```powershell
dotnet restore
dotnet build
dotnet ef database update
dotnet run
```

Swagger endpoints to test:

```http
POST /db-concurrency/reset
POST /demo/db-concurrency/stock/after
POST /db-transaction/reset
POST /db-transaction/checkout/after
```
