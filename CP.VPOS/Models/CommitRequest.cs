using CP.VPOS.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CP.VPOS.Models
{
    public class CommitRequest
    {
        public string orderNumber { get; set; }
        public string orderId { get; set; }
    }
}
