using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using System;
using System.Collections.Generic;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Transactions

{
    internal class RevitTransactionContext : ITransactionContext
    {
        #region Fields
        private readonly Func<Document?> _doc;
        private Transaction? _tx;
        private SuppressWarningFailuresPreprocessor? _warningPreprocessor;
        #endregion

        #region Properties
        #endregion

        #region Constructor
        public RevitTransactionContext(Func<Document?> doc)
        {
            _doc = doc;
        }
        #endregion

        #region Properties
        public void Begin(string name, bool suppressWarnings = false)
        {
            var doc = _doc();
            if (doc == null)
                throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");

            if(_tx != null) throw new DhbimwaterException(ErrorCodes.TransactionAlreadyStarted, "이미 트랜잭션이 시작되었습니다.");

            _tx = new Transaction(doc, name);
            if (suppressWarnings)
            {
                _warningPreprocessor = new SuppressWarningFailuresPreprocessor();
                var options = _tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(_warningPreprocessor);
                _tx.SetFailureHandlingOptions(options);
            }
            _tx.Start();
        }

        public IReadOnlyList<string> SuppressedWarnings => _warningPreprocessor is null ? Array.Empty<string>() : _warningPreprocessor.Messages;

        public void Commit()
        {
            if (_tx == null)
                throw new DhbimwaterException(ErrorCodes.TransactionNotStarted, "트랜잭션이 시작되지 않았습니다.");

            // Revit은 실패 처리(사용자가 오류창에서 취소 등)로 커밋이 무산돼도 예외를 던지지 않고 상태만 돌려준다.
            // 이 값을 버리면 UseCase가 "완료"를 보고한다(docs/205 § B-1).
            var status = _tx.Commit();
            _tx.Dispose();
            // Commit 뒤 예외가 나면 catch의 Rollback()이 폐기된 객체를 다시 만진다 — 먼저 비운다.
            _tx = null;
            if (status != TransactionStatus.Committed)
                throw new DhbimwaterException(ErrorCodes.TransactionNotCommitted,
                    $"Revit이 변경을 커밋하지 않았습니다(상태: {status}). 오류 대화상자에서 취소했거나 Revit이 변경을 거부했습니다.");
        }

        public void Rollback()
        {
            if(_tx != null && _tx.HasStarted())
            {
                _tx.RollBack();
            }
            Dispose();
        }

        public void Dispose()
        {
            if (_tx != null)
            {
                _tx.Dispose();
                _tx = null;
            }
        }
        #endregion
    }
}
