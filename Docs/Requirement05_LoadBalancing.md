# Requirement 05 - Load Balancing

## الهدف

الهدف من هذا المتطلب هو محاكاة توزيع الطلبات على أكثر من خادم، مع توضيح الاستراتيجية المختارة وتبريرها.

في أنظمة التجارة الإلكترونية عالية الضغط، إرسال كل الطلبات إلى خادم واحد يؤدي إلى:

- ضغط زائد على خادم واحد.
- بقاء خوادم أخرى بدون استخدام.
- زيادة زمن الاستجابة عند ارتفاع عدد المستخدمين.
- تحويل خادم واحد إلى نقطة اختناق Single Bottleneck.
- زيادة احتمال فشل النظام إذا أصبح هذا الخادم غير صحي.

لذلك قمنا بمحاكاة Load Balancer أمام ثلاثة خوادم:

- Server A
- Server B
- Server C

كل الخوادم في المحاكاة متشابهة بالقدرة، لذلك استراتيجية Round Robin مناسبة كبداية لأنها بسيطة وعادلة.

---

## الحلول المقترحة

### 1. Single Server - بدون Load Balancing

كل الطلبات تذهب إلى Server A فقط.

- الميزة: بسيط جداً.
- المشكلة: يسبب ضغطاً على خادم واحد ويترك باقي الخوادم بلا عمل.

### 2. Round Robin

توزيع الطلبات بالتسلسل على الخوادم الصحية:

```text
Request 1 -> Server A
Request 2 -> Server B
Request 3 -> Server C
Request 4 -> Server A
...
```

- الميزة: سهل، عادل، مناسب عندما تكون الخوادم متشابهة.
- العيب: لا يراعي اختلاف قوة الخوادم أو عدد الاتصالات النشطة.

### 3. Least Connections

إرسال الطلب إلى الخادم الذي لديه أقل عدد من الاتصالات النشطة.

- الميزة: أفضل عندما تكون مدة الطلبات مختلفة.
- العيب: أكثر تعقيداً من Round Robin.

### 4. Weighted Round Robin

إعطاء وزن أعلى للخوادم الأقوى.

- الميزة: مناسب إذا كانت الخوادم غير متساوية.
- العيب: يحتاج معرفة مسبقة بقدرة كل خادم.

---

## الحل المختار

تم اختيار:

```text
Round Robin over healthy servers
```

سبب الاختيار:

- الخوادم في المحاكاة متشابهة.
- عدد الطلبات متقارب بالزمن والحجم.
- الاستراتيجية واضحة وسهلة القياس في JMeter.
- تحقق المطلوب الأساسي: توزيع الطلبات على أكثر من خادم بدل تركيزها على خادم واحد.

تم أيضاً إضافة مفهوم Health Check بشكل مبسط: إذا تم تعليم خادم كـ Unhealthy فإن الـ Load Balancer يتجاهله ويرسل الطلبات إلى الخوادم الصحية فقط.

---

## Endpoints المستخدمة

### إعادة ضبط القياسات

```http
POST /load-balancer/reset
```

يعيد جميع العدادات للصفر، ويجعل كل الخوادم Healthy.

### عرض القياسات

```http
GET /load-balancer/metrics
```

يعرض:

- الاستراتيجية الحالية.
- عدد الطلبات الكلي.
- عدد الطلبات الفاشلة.
- عدد الطلبات النشطة حالياً.
- عدد الخوادم الصحية وغير الصحية.
- عدد الطلبات التي عالجها كل خادم.
- أعلى عدد طلبات نشطة على كل خادم.
- متوسط زمن المعالجة لكل خادم.

### عرض الخوادم

```http
GET /load-balancer/servers
```

يعرض Snapshot عن الخوادم الثلاثة.

### تغيير حالة خادم

```http
POST /load-balancer/servers/{serverKey}/health?isHealthy=false
```

أمثلة:

```http
POST /load-balancer/servers/server-b/health?isHealthy=false
POST /load-balancer/servers/server-b/health?isHealthy=true
```

### طلب واحد قبل التوزيع

```http
POST /load-balancer/before/request
```

هذا endpoint مخصص لـ JMeter. كل طلب يذهب إلى Server A فقط.

### طلب واحد بعد التوزيع

```http
POST /load-balancer/after/request
```

هذا endpoint مخصص لـ JMeter. كل طلب يختار الخادم التالي باستخدام Round Robin.

### Demo سريع قبل الحل

```http
POST /demo/load-balancing/before
```

يشغل 30 طلباً داخلياً، وكلها تذهب إلى Server A.

### Demo سريع بعد الحل

```http
POST /demo/load-balancing/after
```

يشغل 30 طلباً داخلياً ويوزعها على الخوادم الثلاثة.

### Demo مع Health Check

```http
POST /demo/load-balancing/after-health-check
```

يقوم بتعليم Server B كـ Unhealthy، ثم يوزع الطلبات فقط على Server A و Server C.

---

## Before Optimization - No Load Balancing

في النسخة قبل التحسين:

```http
POST /demo/load-balancing/before
```

كل الطلبات يتم إرسالها إلى Server A.

النتيجة المتوقعة:

```text
Server A: 30 requests
Server B: 0 requests
Server C: 0 requests
```

### التحليل

هذا يثبت أن البنية قبل التحسين لا تستخدم الموارد المتاحة بشكل عادل. Server A يصبح نقطة اختناق، بينما Server B و Server C لا يستقبلان أي عمل.

---

## After Optimization - Round Robin

في النسخة بعد التحسين:

```http
POST /demo/load-balancing/after
```

النتيجة المتوقعة عند 30 طلباً:

```text
Server A: 10 requests
Server B: 10 requests
Server C: 10 requests
```

### التحليل

هذا يثبت أن الطلبات توزعت بالتساوي على الخوادم الثلاثة. بدل أن يتحمل خادم واحد كامل الضغط، أصبح كل خادم يعالج ثلث الحمل تقريباً.

---

## Health Check Simulation

تمت إضافة endpoint:

```http
POST /demo/load-balancing/after-health-check
```

في هذا الاختبار يتم اعتبار Server B غير صحي. النتيجة المتوقعة عند 30 طلباً:

```text
Server A: 15 requests
Server B: 0 requests, unhealthy
Server C: 15 requests
```

هذا يثبت أن الـ Load Balancer لا يرسل الطلبات إلى خادم غير صحي.

---

## كيف سنختبره لاحقاً في JMeter

الاختبار الرسمي سيكون كالتالي:

1. تشغيل:

```http
POST /load-balancer/reset
```

2. إرسال 30 أو 90 طلباً متزامناً إلى:

```http
POST /load-balancer/before/request
```

ثم قراءة:

```http
GET /load-balancer/metrics
```

المتوقع: Server A فقط يعالج الطلبات.

3. إعادة الضبط:

```http
POST /load-balancer/reset
```

4. إرسال 30 أو 90 طلباً متزامناً إلى:

```http
POST /load-balancer/after/request
```

ثم قراءة:

```http
GET /load-balancer/metrics
```

المتوقع: الطلبات تتوزع على Server A و Server B و Server C.

---

## مقارنة قبل وبعد

| الحالة | الاستراتيجية | Server A | Server B | Server C | النتيجة |
|---|---|---:|---:|---:|---|
| Before | Single Server | 30 | 0 | 0 | ضغط كامل على خادم واحد |
| After | Round Robin | 10 | 10 | 10 | توزيع متوازن للطلبات |
| After + Health Check | Round Robin over healthy servers | 15 | 0 | 15 | تجاهل الخادم غير الصحي |

---

## الخلاصة

تم تحقيق المتطلب الخامس عبر بناء محاكاة واضحة للـ Load Balancing:

- قبل الحل: كل الطلبات تذهب إلى خادم واحد فقط.
- بعد الحل: الطلبات تتوزع على الخوادم الصحية باستخدام Round Robin.
- تم دعم Health Check Simulation لإثبات أن الخادم غير الصحي لا يستقبل طلبات جديدة.
- تم توفير endpoints قابلة لاختبار JMeter لاحقاً، وليس فقط Demo يدوي.
