# Requirement 01 - Shared Data Protection & Race Condition Handling

## الهدف

الهدف من هذا المتطلب هو إثبات مشكلة Race Condition عند وصول عدة مستخدمين بنفس الوقت إلى نفس المورد المشترك، وهو مخزون المنتج، ثم تطبيق حل يمنع تضارب البيانات.

في هذا المشروع، المورد المشترك هو:

- Product Id: 1
- Product Name: Gaming Laptop
- Initial Stock: 10

تمت محاكاة 20 مستخدم متزامن يحاولون شراء نفس المنتج، كل مستخدم يشتري قطعة واحدة.

---

## Before Optimization - Unsafe Implementation

في النسخة الأولى استخدمنا الدالة:

```csharp
PurchaseBeforeAsync(productId, quantity)