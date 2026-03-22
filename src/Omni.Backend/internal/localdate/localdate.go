package localdate

import (
	"fmt"
	"strconv"

	"github.com/gin-gonic/gin"
)

const maxAbsOffsetMinutes = 14 * 60

// OffsetMinutes parses utc_offset_minutes; invalid or missing defaults to 0 (UTC calendar day).
func OffsetMinutes(c *gin.Context) int {
	s := c.Query("utc_offset_minutes")
	if s == "" {
		return 0
	}
	n, err := strconv.Atoi(s)
	if err != nil {
		return 0
	}
	if n < -maxAbsOffsetMinutes {
		return -maxAbsOffsetMinutes
	}
	if n > maxAbsOffsetMinutes {
		return maxAbsOffsetMinutes
	}
	return n
}

// SQLExpr returns a parameterized SQL expression for the local calendar date of timestamptz col,
// using the offset in minutes at parameter index offsetParam (e.g. 2 -> $2).
func SQLExpr(col string, offsetParam int) string {
	return fmt.Sprintf("date((%s AT TIME ZONE 'UTC') + (interval '1 minute' * $%d::integer))", col, offsetParam)
}
