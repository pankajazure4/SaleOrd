-- ============================================================================
-- Step 1 — Company ka naam confirm karo (CompanyId yahan se pata chalega)
-- ============================================================================
SELECT CompanyId, CompanyName, TallyCompanyName FROM Companies;

-- ============================================================================
-- Step 2 — Is company mein Godowns table mein kitne godowns hain, aur unke
-- naam genuinely kaise dikhte hain? (Company name yaha edit kar do)
-- ============================================================================
SELECT COUNT(*) AS TotalGodowns
FROM Godowns g JOIN Companies c ON c.CompanyId = g.CompanyId
WHERE c.CompanyName = 'COMPANY NAME YAHA DAALO';

SELECT TOP 40 g.GodownId, g.GodownName, g.Parent
FROM Godowns g JOIN Companies c ON c.CompanyId = g.CompanyId
WHERE c.CompanyName = 'COMPANY NAME YAHA DAALO'
ORDER BY g.GodownName;

-- ============================================================================
-- Step 3 — Screenshot wale specific item ke liye poora godown-wise
-- breakdown dekho — yeh confirm karega kitne godowns genuinely is item
-- se juде hain aur unke naam kya hain
-- ============================================================================
SELECT b.GodownName, b.ClosingBalance, b.ClosingRate, b.ClosingValue
FROM StockItemGodownBalances b
JOIN StockItems s ON s.StockItemId = b.StockItemId
JOIN Companies c ON c.CompanyId = b.CompanyId
WHERE s.ItemName = 'Amul Butter (RP) 500gm' AND c.CompanyName = 'COMPANY NAME YAHA DAALO'
ORDER BY b.GodownName;

SELECT COUNT(*) AS GodownRowsForThisItem
FROM StockItemGodownBalances b
JOIN StockItems s ON s.StockItemId = b.StockItemId
JOIN Companies c ON c.CompanyId = b.CompanyId
WHERE s.ItemName = 'Amul Butter (RP) 500gm' AND c.CompanyName = 'COMPANY NAME YAHA DAALO';

-- ============================================================================
-- Step 4 — SABSE IMPORTANT: kya StockItemGodownBalances ke GodownName
-- genuinely Godowns table mein bhi maujood hain? Agar yeh query kuch bhi
-- return kare, toh matlab garbage/orphaned names ban rahe hain (bug).
-- Agar khaali aaye, toh sab genuine, real Godowns hain (bug nahi hai).
-- ============================================================================
SELECT DISTINCT b.GodownName, COUNT(*) OVER (PARTITION BY b.GodownName) AS UsedByHowManyItems
FROM StockItemGodownBalances b
JOIN Companies c ON c.CompanyId = b.CompanyId
LEFT JOIN Godowns g ON g.CompanyId = b.CompanyId AND g.GodownName = b.GodownName
WHERE c.CompanyName = 'COMPANY NAME YAHA DAALO' AND g.GodownId IS NULL;

-- ============================================================================
-- Step 5 — Overall sanity: total rows in StockItemGodownBalances vs total
-- items vs total godowns — agar rows = items x godowns (roughly), matlab
-- genuinely har item har godown mein hai (real data). Agar bahut zyada
-- (duplicate-jaisa) hai, toh kuch galat hai sync mein.
-- ============================================================================
SELECT
    (SELECT COUNT(*) FROM StockItemGodownBalances b JOIN Companies c ON c.CompanyId = b.CompanyId WHERE c.CompanyName = 'COMPANY NAME YAHA DAALO') AS TotalBalanceRows,
    (SELECT COUNT(*) FROM StockItems s JOIN Companies c ON c.CompanyId = s.CompanyId WHERE c.CompanyName = 'COMPANY NAME YAHA DAALO') AS TotalStockItems,
    (SELECT COUNT(*) FROM Godowns g JOIN Companies c ON c.CompanyId = g.CompanyId WHERE c.CompanyName = 'COMPANY NAME YAHA DAALO') AS TotalGodowns;
