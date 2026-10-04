using Fitmate.Application.Chat;
using Fitmate.Domain.Chat;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class AiChatServiceTests
{
    private sealed class Store : IAiChatStore
    {
        public Dictionary<Guid, ChatThread> Threads { get; } = new();
        public List<ChatMessage> Messages { get; } = new();
        public Task<IReadOnlyList<AiThreadSummaryRow>> ListThreadsAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AiThreadSummaryRow>>(Threads.Values.Where(x => x.MemberId == userId && !x.IsDeleted).Select(x => new AiThreadSummaryRow(x, Messages.Count(m => m.ThreadId == x.Id))).ToList());
        public Task<ChatThread?> FindThreadAsync(Guid threadId, CancellationToken ct) =>
            Task.FromResult(Threads.GetValueOrDefault(threadId) is { IsDeleted: false } thread ? thread : null);
        public Task AddThreadAsync(ChatThread thread, CancellationToken ct) { Threads[thread.Id] = thread; return Task.CompletedTask; }
        public Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(Guid threadId, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(x => x.ThreadId == threadId).TakeLast(limit).ToList());
        public Task AddMessageAsync(ChatMessage message, CancellationToken ct) { Messages.Add(message); return Task.CompletedTask; }
        public Task RemoveMessagesAsync(Guid threadId, CancellationToken ct) { Messages.RemoveAll(x => x.ThreadId == threadId); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Adapter : IAiCompanionAdapter
    {
        public Task<CompanionChatResponseView> GenerateReplyAsync(CompanionChatRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("Adapter must not be called by thread management tests.");
    }

    [Fact]
    public async Task RenameThread_Owner_UpdatesTrimmedTitle()
    {
        var owner = Guid.NewGuid();
        var thread = ChatThread.CreateAi(owner, "Cũ");
        var store = new Store();
        store.Threads[thread.Id] = thread;

        var result = await new AiChatService(store, new Adapter()).RenameThreadAsync(owner, thread.Id, "  Tên mới  ", default);

        result.Title.Should().Be("Tên mới");
        thread.Title.Should().Be("Tên mới");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenameThread_InvalidTitle_ThrowsValidation(string title)
    {
        var service = new AiChatService(new Store(), new Adapter());
        var action = () => service.RenameThreadAsync(Guid.NewGuid(), Guid.NewGuid(), title, default);
        await action.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task DeleteThread_Owner_DeletesMessagesAndHidesThread()
    {
        var owner = Guid.NewGuid();
        var thread = ChatThread.CreateAi(owner, "Cần xóa");
        var store = new Store();
        store.Threads[thread.Id] = thread;
        store.Messages.Add(ChatMessage.CreateUser(thread.Id, owner, "Xin chào"));

        await new AiChatService(store, new Adapter()).DeleteThreadAsync(owner, thread.Id, default);

        thread.IsDeleted.Should().BeTrue();
        store.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteThread_NonOwner_ReturnsNotFoundAndKeepsData()
    {
        var owner = Guid.NewGuid();
        var thread = ChatThread.CreateAi(owner, "Riêng tư");
        var store = new Store();
        store.Threads[thread.Id] = thread;

        var action = () => new AiChatService(store, new Adapter()).DeleteThreadAsync(Guid.NewGuid(), thread.Id, default);

        await action.Should().ThrowAsync<NotFoundException>();
        thread.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Send_ChineseMessage_ReturnsLanguageNoticeWithoutCallingProvider()
    {
        var owner = Guid.NewGuid();
        var thread = ChatThread.CreateAi(owner, "Language policy");
        var store = new Store();
        store.Threads[thread.Id] = thread;

        var result = await new AiChatService(store, new Adapter())
            .SendAsync(owner, thread.Id, "请给我一个训练计划", null, null, default);

        result.Text.Should().Be("Hiện tại Fitmate AI chỉ hỗ trợ tiếng Việt và tiếng Anh. Please continue in Vietnamese or English.");
        store.Messages.Should().HaveCount(2);
    }
}
