# Requirement 06 - Distributed Caching Strategy

## الهدف

الهدف من هذا المتطلب هو استخدام الـ Cache في أكثر من مكان داخل Backend حتى نقلل الضغط على مصدر البيانات ونسرع العمليات كثيرة القراءة في منظومة التجارة الإلكترونية.

حسب نص المتطلب، المطلوب هو دمج طبقة تخزين مؤقت مثل Redis لتخزين البيانات الأكثر طلباً وتقليل الاستعلامات المباشرة من قاعدة البيانات.

في هذا المشروع طبقنا نمط:

```text
Cache-Aside Pattern
```

أي أن النظام يقرأ من الـ Cache أولاً. إذا وجد القيمة يرجعها مباشرة، وإذا لم يجدها يقرأ من مصدر البيانات، ثم يحفظ النتيجة داخل الـ Cache للطلبات القادمة.

---

## المشكلة

في نظام التجارة الإلكترونية توجد عمليات قراءة تتكرر كثيراً جداً، مثل:

- قراءة تفاصيل منتج مطلوب بكثرة.
- قراءة قائمة المنتجات الأكثر طلباً.
- قراءة كمية المخزون الحالية للعرض السريع.

بدون Cache، كل طلب يذهب إلى مصدر البيانات حتى لو كان يطلب نفس النتيجة التي طلبها مستخدم آخر قبل لحظات. هذا يؤدي إلى:

- زيادة الضغط على قاعدة البيانات.
- زيادة زمن الاستجابة.
- ظهور اختناق عند زيادة عدد المستخدمين.
- استهلاك غير ضروري للموارد في عمليات قراءة مكررة.

---

## الحلول المقترحة

| الحل | التقييم |
|---|---|
| بدون Cache | بسيط لكنه يسبب ضغطاً متكرراً على مصدر البيانات |
| In-Memory Cache داخل Instance واحدة | سريع ومفيد للتجربة، لكنه لا يشارك البيانات بين أكثر من خادم |
| Distributed Cache مثل Redis | مناسب عند وجود أكثر من instance لأنه يجعل الكاش مشتركاً ومركزياً |
| CDN Cache | مناسب للملفات الثابتة وليس مناسباً مباشرة لتحديثات المخزون والطلبات |

---

## هل البنية الحالية تدعم الحل؟

نعم. البنية الحالية مفصولة إلى Endpoints و Services و Models، لذلك أضفنا خدمة مستقلة للكاش بدون وضع المنطق داخل الـ endpoints مباشرة:

```text
Services/CachingCatalogService.cs
Endpoints/CachingEndpoints.cs
Models/CacheMetricsSnapshot.cs
Models/StockSnapshot.cs
```

كما تم استخدام:

```text
IDistributedCache
```

بدلاً من الاعتماد على نوع محدد. هذا يجعل التطبيق يعمل محلياً باستخدام DistributedMemoryCache، ويمكن تحويله إلى Redis من خلال الإعدادات فقط.

---

## الحل المختار

تم اختيار:

```text
Cache-Aside using IDistributedCache, with Redis-ready configuration
```

### سبب الاختيار

- مناسب جداً للقراءات المتكررة.
- لا يعقد عمليات الكتابة كثيراً.
- يسمح بقياس واضح قبل/بعد.
- يدعم Redis عند تشغيله، مع fallback محلي للتجربة عندما لا يكون Redis متوفراً.
- يعطي نقاط Invalidation واضحة عند تعديل المخزون.

---

## أين استخدمنا الكاش؟

تم استخدام الكاش في أكثر من مكان كما طلب الدكتور:

### 1. تفاصيل المنتج

Endpoint بعد التحسين:

```http
GET /cache/after/products/{productId}
```

المفتاح المستخدم:

```text
products:details:{productId}
```

مدة التخزين:

```text
Absolute Expiration = 2 minutes
Sliding Expiration = 30 seconds
```

---

### 2. قائمة المنتجات الأكثر طلباً

Endpoint بعد التحسين:

```http
GET /cache/after/popular-products?count=3
```

المفتاح المستخدم:

```text
products:popular:top:{count}
```

مدة التخزين:

```text
Absolute Expiration = 5 minutes
Sliding Expiration = 1 minute
```

هذه القائمة مناسبة للكاش لأنها كثيرة القراءة ولا تتغير مع كل طلب شراء بنفس درجة تغيّر المخزون.

---

### 3. لقطة المخزون السريعة

Endpoint بعد التحسين:

```http
GET /cache/after/stock/{productId}
```

المفتاح المستخدم:

```text
inventory:stock:{productId}
```

مدة التخزين قصيرة:

```text
Absolute Expiration = 20 seconds
Sliding Expiration = 10 seconds
```

سبب قصر المدة أن المخزون حساس ويتغير مع عمليات الشراء.

---

## منع البيانات القديمة Stale Data

عند تعديل المخزون يجب حذف مفاتيح الكاش المتعلقة بالمنتج حتى لا يرجع النظام بيانات قديمة.

تم ربط الـ invalidation مع:

```http
POST /inventory/reset
POST /purchase/after
POST /demo/race/after
POST /cache/invalidate/product/{productId}
```

عند نجاح شراء منتج أو تصفير مخزونه، يتم حذف:

```text
products:details:{productId}
inventory:stock:{productId}
products:popular:top:{count}
```

---

## الملفات التي تمت إضافتها أو تعديلها

### ملفات جديدة

```text
Services/CachingCatalogService.cs
Endpoints/CachingEndpoints.cs
Models/CacheMetricsSnapshot.cs
Models/StockSnapshot.cs
Docs/Requirement06_Caching.md
Tests/JMeter/Requirement06_Caching.jmx
Tests/JMeter/README.md
docker-compose.cache.yml
```

### ملفات معدلة

```text
Program.cs
ParallelECommerce.csproj
appsettings.json
appsettings.Development.json
Services/InventoryService.cs
Endpoints/InventoryEndpoints.cs
Models/Product.cs
ParallelECommerce.http
```

---

## Endpoints الخاصة بالطلب السادس

### Reset

```http
POST /cache/reset
```

يعيد عدادات الكاش ويحذف المفاتيح المعروفة.

### Metrics

```http
GET /cache/metrics
```

يعرض:

- نوع Provider المستخدم.
- عدد hits/misses لكل مكان.
- عدد القراءات المباشرة من مصدر البيانات.
- عدد القراءات من مصدر البيانات بعد cache miss.
- عدد عمليات invalidation.

### قبل التحسين

```http
GET /cache/before/products/1
GET /cache/before/popular-products?count=3
GET /cache/before/stock/1
POST /demo/cache/before
```

### بعد التحسين

```http
GET /cache/after/products/1
GET /cache/after/popular-products?count=3
GET /cache/after/stock/1
POST /demo/cache/after
```

---

## الاختبار

### Demo قبل الحل

Endpoint:

```http
POST /demo/cache/before
```

يقوم بمحاكاة:

```text
30 reads for product details
10 reads for popular products
10 reads for stock snapshot
```

النتيجة المتوقعة قبل الحل:

```text
DirectDatabaseReads = 50
ProductCacheHits = 0
PopularProductsCacheHits = 0
StockCacheHits = 0
```

أي أن كل قراءة ذهبت إلى مصدر البيانات.

---

### Demo بعد الحل

Endpoint:

```http
POST /demo/cache/after
```

يقوم بنفس عدد القراءات، لكن باستخدام Cache-Aside.

النتيجة المتوقعة بعد الحل:

```text
ProductCacheMisses = 1
ProductCacheHits = 29
PopularProductsCacheMisses = 1
PopularProductsCacheHits = 9
StockCacheMisses = 1
StockCacheHits = 9
CacheBackedDatabaseReads = 3
```

أي أن أول طلب لكل مفتاح يذهب إلى مصدر البيانات، ثم يتم تخديم باقي الطلبات من الكاش.

---

## ملف JMeter

تمت إضافة ملف الاختبار:

```text
Tests/JMeter/Requirement06_Caching.jmx
```

الاختبار يحتوي على:

- Reset للـ monitoring والـ cache.
- Demo قبل الحل.
- Metrics قبل الحل.
- Demo بعد الحل.
- Metrics بعد الحل.
- ضغط متزامن على endpoints بعد التحسين.
- Summary Report و View Results Tree.

---

## الصور المطلوبة للتسليم

عند تشغيل الاختبار النهائي، نحفظ الصور التالية:

```text
Docs/Screenshots/Requirement06_Caching/06_cache_before_demo.png
Docs/Screenshots/Requirement06_Caching/06_cache_after_demo.png
Docs/Screenshots/Requirement06_Caching/06_cache_metrics.png
Docs/Screenshots/Requirement06_Caching/06_jmeter_summary.png
```

---

## تشغيل Redis اختيارياً

الوضع الافتراضي يعمل بدون Redis حتى لا يتعطل التشغيل المحلي:

```json
"Cache": {
  "UseRedis": false,
  "RedisConnectionString": "localhost:6379"
}
```

لتشغيل Redis محلياً:

```bash
docker compose -f docker-compose.cache.yml up -d
```

ثم تغيير الإعدادات:

```json
"UseRedis": true
```

بعدها يستخدم التطبيق Redis من خلال نفس الكود لأن الاعتماد هو على `IDistributedCache`.

---

## الخلاصة

تم تحقيق المتطلب السادس لأن المشروع أصبح يستخدم Cache في أكثر من مكان، ويقيس hit/miss، ويقدم مقارنة واضحة بين القراءة المباشرة قبل الحل والقراءة من الكاش بعد الحل، مع وجود آلية Invalidation عند تعديل المخزون.
