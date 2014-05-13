using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Dashboard.Data
{
    public class FunctionInstanceSnapshot
    {
        private readonly FunctionInstanceEntity _instanceEntity;
        private readonly IDictionary<string, FunctionInstanceArgument> _arguments;

        [CLSCompliant(false)]
        public FunctionInstanceSnapshot(FunctionInstanceEntity instanceEntity, IEnumerable<FunctionArgumentEntity> argumentEntities)
        {
            if (instanceEntity == null)
            {
                throw new ArgumentNullException("instanceEntity");
            }
            else if (argumentEntities == null)
            {
                throw new ArgumentNullException("argumentEntities");
            }

            _instanceEntity = instanceEntity;
            _arguments = new Dictionary<string, FunctionInstanceArgument>();

            foreach (FunctionArgumentEntity argumentEntity in argumentEntities)
            {
                _arguments.Add(argumentEntity.Name, new FunctionInstanceArgument(argumentEntity));
            }
        }

        public Guid Id
        {
            get { return _instanceEntity.Id; }
        }

        public Guid HostInstanceId
        {
            get { return _instanceEntity.HostInstanceId; }
        }

        public string FunctionId
        {
            get { return _instanceEntity.FunctionId; }
        }

        public string FunctionFullName
        {
            get { return _instanceEntity.FunctionFullName; }
        }

        public string FunctionShortName
        {
            get { return _instanceEntity.FunctionShortName; }
        }

        public IDictionary<string, FunctionInstanceArgument> Arguments
        {
            get { return _arguments; }
        }

        public Guid? ParentId
        {
            get { return _instanceEntity.ParentId; }
        }

        public string Reason
        {
            get { return _instanceEntity.Reason; }
        }

        public DateTimeOffset QueueTime
        {
            get { return _instanceEntity.QueueTime; }
        }

        public DateTimeOffset? StartTime
        {
            get { return _instanceEntity.StartTime; }
        }

        public DateTimeOffset? EndTime
        {
            get { return _instanceEntity.EndTime; }
        }

        public string StorageConnectionString
        {
            get { return _instanceEntity.StorageConnectionString; }
        }

        public string OutputBlobUrl
        {
            get { return _instanceEntity.OutputBlobUrl; }
        }

        public string ParameterLogBlobUrl
        {
            get { return _instanceEntity.ParameterLogBlobUrl; }
        }

        public string WebSiteName
        {
            get { return _instanceEntity.WebSiteName; }
        }

        public string WebJobType
        {
            get { return _instanceEntity.WebJobType; }
        }

        public string WebJobName
        {
            get { return _instanceEntity.WebJobName; }
        }

        public string WebJobRunId
        {
            get { return _instanceEntity.WebJobRunId; }
        }

        public bool? Succeeded
        {
            get { return _instanceEntity.Succeeded; }
        }

        public string ExceptionType
        {
            get { return _instanceEntity.ExceptionType; }
        }

        public string ExceptionMessage
        {
            get { return _instanceEntity.ExceptionMessage; }
        }
    }
}