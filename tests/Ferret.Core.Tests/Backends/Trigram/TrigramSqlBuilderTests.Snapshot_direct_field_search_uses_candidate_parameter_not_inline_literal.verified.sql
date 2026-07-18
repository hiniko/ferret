WITH candidates AS (SELECT unnest(@candidate_ids) AS "id"),
field_matches AS (
  SELECT e."id", @p0 <<-> (e."name")::text AS distance
  FROM "products" e
  INNER JOIN candidates cnd ON cnd."id" = e."id"
  UNION ALL
  SELECT e."id", @p1 <<-> (e."sku")::text AS distance
  FROM "products" e
  INNER JOIN candidates cnd ON cnd."id" = e."id"
),
search_results AS (
  SELECT "id", MIN(distance) AS distance FROM field_matches GROUP BY "id")
SELECT sr."id", COUNT(*) OVER() AS total_count FROM search_results sr
WHERE sr.distance <= 0.65
ORDER BY sr.distance, sr."id"
LIMIT 25 OFFSET 0