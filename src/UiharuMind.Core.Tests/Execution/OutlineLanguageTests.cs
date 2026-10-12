using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉住 C#/C++ 以外各语言的大纲口径：列类型与函数、不列字段与局部函数，各语言字面量里的括号不算数
/// </summary>
public sealed class OutlineLanguageTests
{
    private static List<OutlineEntry> Extract(IOutlineExtractor extractor, string code) =>
        extractor.Extract(code.Split('\n')).ToList();

    [Fact]
    public void Java_ListsTypesAndMethods_NotFieldsOrAnonymousClasses()
    {
        List<OutlineEntry> entries = Extract(new JavaOutlineExtractor(), """"
            package demo;

            @Service
            public final class Foo<T> extends Bar implements Baz {
                private int count = compute();
                private final Runnable task = new Runnable() {
                    public void run() { }
                };
                static { init(); }

                @Override
                public <R> List<R> map(Function<T, R> fn) throws IOException {
                    String s = """
                        { ; }
                        """;
                    return null;
                }

                abstract void stop();

                public enum EState { IDLE, BUSY }
            }
            """");

        Assert.Equal(
        [
            new OutlineEntry(4, 0, "public final class Foo<T> extends Bar implements Baz", 22),
            new OutlineEntry(12, 1, "public <R> List<R> map(Function<T, R> fn) throws IOException", 17),
            new OutlineEntry(19, 1, "abstract void stop()", 19),
            new OutlineEntry(21, 1, "public enum EState", 21),
        ], entries);
    }

    [Fact]
    public void TypeScript_ListsDeclarations_SurvivesTemplatesRegexAndDestructuring()
    {
        List<OutlineEntry> entries = Extract(new TypeScriptOutlineExtractor(), """
            import { a } from 'x'
            const pattern = /[{]/g
            export interface Props {
              name: string
              render(): void
            }

            export const App = ({ name, items }: Props) => {
              const label = `${name} {`
              return null
            }

            describe('suite', () => {
              it('works', () => {})
            })

            export default class Store extends Base {
              #count = 0
              @observable items = []
              constructor(private api: Api) { super() }
              async load(id: string): Promise<void> {
                if (id) { }
              }
              handle = (e: Event) => { }
              get size() { return 1 }
            }

            function helper(a,
                            b) {
            }
            """);

        Assert.Equal(
        [
            new OutlineEntry(3, 0, "export interface Props", 6),
            new OutlineEntry(5, 1, "render(): void", 5),
            new OutlineEntry(8, 0, "export const App = ({ name, items }: Props)", 11),
            new OutlineEntry(17, 0, "export default class Store extends Base", 26),
            new OutlineEntry(20, 1, "constructor(private api: Api)", 20),
            new OutlineEntry(21, 1, "async load(id: string): Promise<void>", 23),
            new OutlineEntry(24, 1, "handle = (e: Event)", 24),
            new OutlineEntry(25, 1, "get size()", 25),
            new OutlineEntry(28, 0, "function helper(a, b)", 30),
        ], entries);
    }

    [Fact]
    public void Go_ListsTypesFuncsAndInterfaceMethods_NotStructFields()
    {
        List<OutlineEntry> entries = Extract(new GoOutlineExtractor(), """
            package main

            import (
                "fmt"
            )

            type Server struct {
                addr string
                mu   sync.Mutex
            }

            type Reader interface {
                Read(p []byte) (int, error)
            }

            type ID int

            var raw = `{ not a block`

            func (s *Server) Start(ctx context.Context) error {
                if s == nil {
                    return nil
                }
                return nil
            }
            """);

        Assert.Equal(
        [
            new OutlineEntry(7, 0, "type Server struct", 10),
            new OutlineEntry(12, 0, "type Reader interface", 14),
            new OutlineEntry(13, 1, "Read(p []byte) (int, error)", 13),
            new OutlineEntry(16, 0, "type ID int", 16),
            new OutlineEntry(20, 0, "func (s *Server) Start(ctx context.Context) error", 25),
        ], entries);
    }

    [Fact]
    public void Rust_ListsItems_LifetimesAndRawStringsDoNotConfuse()
    {
        List<OutlineEntry> entries = Extract(new RustOutlineExtractor(), """
            use std::fmt;

            #[derive(Debug, Clone)]
            pub struct Parser<'a> {
                input: &'a str,
            }

            pub struct Unit;

            impl<'a> Parser<'a> {
                pub fn new(input: &'a str) -> Self {
                    let s = r#"{ "a": 1 }"#;
                    let c = '{';
                    Self { input }
                }
            }

            pub trait Visit {
                fn visit(&self);
            }

            mod inner {
                pub(crate) async fn run() {}
            }
            """);

        Assert.Equal(
        [
            new OutlineEntry(4, 0, "pub struct Parser<'a>", 6),
            new OutlineEntry(8, 0, "pub struct Unit", 8),
            new OutlineEntry(10, 0, "impl<'a> Parser<'a>", 16),
            new OutlineEntry(11, 1, "pub fn new(input: &'a str) -> Self", 15),
            new OutlineEntry(18, 0, "pub trait Visit", 20),
            new OutlineEntry(19, 1, "fn visit(&self)", 19),
            new OutlineEntry(23, 0, "pub(crate) async fn run()", 23),
        ], entries);
    }

    [Fact]
    public void Python_ListsClassesAndMethods_NotNestedFunctionsOrDocstrings()
    {
        List<OutlineEntry> entries = Extract(new PythonOutlineExtractor(), """"
            import os

            class Foo(Base):
                """
                def fake(): 文档里的示例
                """

                @property
                def name(self) -> str:
                    return self._name

                async def load(self,
                               path: str):  # 跨行签名
                    def inner():
                        pass
                    return inner

            def main():
                pass
            """");

        Assert.Equal(
        [
            new OutlineEntry(3, 0, "class Foo(Base)", 16),
            new OutlineEntry(9, 1, "def name(self) -> str", 10),
            new OutlineEntry(12, 1, "async def load(self, path: str)", 16),
            new OutlineEntry(18, 0, "def main()", 19),
        ], entries);
    }

    [Theory]
    [InlineData("a.java")]
    [InlineData("a.tsx")]
    [InlineData("a.go")]
    [InlineData("a.rs")]
    [InlineData("a.py")]
    public void FileOutline_DispatchesNewLanguages(string fileName)
    {
        Assert.True(FileOutline.Supports(fileName));
    }

    /// <summary>函数以顶格写的多行字符串结尾时，字符串那几行仍属于它</summary>
    [Fact]
    public void Python_MultilineStringAtEndOfFunction_CountsTowardItsEndLine()
    {
        List<OutlineEntry> entries = Extract(new PythonOutlineExtractor(), """"
            def query():
                return """
            SELECT a
            """

            def other():
                pass
            """");

        Assert.Equal([new OutlineEntry(1, 0, "def query()", 4), new OutlineEntry(6, 0, "def other()", 7)], entries);
    }

    /// <summary>不写分号的语言里，类型声明头跨行（extends 换行、泛型参数换行）不能在行尾截断</summary>
    [Fact]
    public void TypeScript_TypeHeaderAcrossLines_KeepsItsBody()
    {
        List<OutlineEntry> entries = Extract(new TypeScriptOutlineExtractor(), """
            export class Foo
              extends Bar {
              run() {}
            }
            class Box<
              T
            > {
              get() {}
            }
            """);

        Assert.Equal(
        [
            new OutlineEntry(1, 0, "export class Foo extends Bar", 4),
            new OutlineEntry(3, 1, "run()", 3),
            new OutlineEntry(5, 0, "class Box<T>", 9),
            new OutlineEntry(8, 1, "get()", 8),
        ], entries);
    }

    [Fact]
    public void Markdown_IndentedCodeBlock_IsNotAHeading()
    {
        List<OutlineEntry> entries = new MarkdownOutlineExtractor().Extract(["# 标题", "", "    # 注释", "\t# 注释"]).ToList();

        Assert.Equal([new OutlineEntry(1, 0, "# 标题", 4)], entries);
    }
}
